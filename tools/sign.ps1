# 给外壳自签名，并让本机信任这张证书。
#
# 做三件事：
#   1) 在「当前用户\个人」里创建一张代码签名证书（10 年有效，只用于本机）
#   2) 把它的公钥导入「本地计算机\受信任的根证书颁发机构」和「受信任的发布者」，
#      这样本机才认为这个签名可信
#   3) 用它对 exe 签名（SHA256）
#
# 撤销：pwsh -File tools\sign.ps1 -Remove   （删除证书与两处信任项）
[CmdletBinding()]
param(
    [string]$Exe = '',
    [switch]$Remove,
    [string]$SubjectName = 'DeepSeek Harness Shell',
    [string]$Issuer = 'DshShell local self-signed'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Exe -eq '') { $Exe = Join-Path $root 'app\DshShell.exe' }

function Get-OurCerts {
    $stores = @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPublisher')
    $found = @()
    foreach ($store in $stores) {
        $found += Get-ChildItem $store -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -like "*$SubjectName*" -or $_.Subject -like "*$Issuer*" }
    }
    return $found
}

if ($Remove) {
    $certs = Get-OurCerts
    if (-not $certs) { Write-Output '没有找到本外壳的证书'; exit 0 }
    foreach ($c in ($certs | Sort-Object Thumbprint -Unique)) {
        try {
            Remove-Item $c.PSPath -Force
            Write-Output "已删除 $($c.PSPath)"
        }
        catch {
            Write-Output "删除失败 $($c.Thumbprint): $($_.Exception.Message)"
        }
    }
    Write-Output '撤销完成（exe 上已有的签名会变成"证书不受信任"状态，重新编译即可去掉）'
    exit 0
}

if (-not (Test-Path $Exe)) { throw "找不到 $Exe，请先编译" }

# 1) 证书
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -like "*$SubjectName*" -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Select-Object -First 1

if (-not $cert) {
    Write-Output '创建代码签名证书…'
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=$SubjectName, O=$Issuer" `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(10) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
}

Write-Output "使用证书: $($cert.Thumbprint)  $($cert.Subject)"

# 2) 本机信任
$cerFile = Join-Path $env:TEMP 'dshshell-codesign.cer'
foreach ($store in @('Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPublisher')) {
    $exists = Get-ChildItem $store -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if ($exists) {
        Write-Output "已信任: $store"
        continue
    }

    Export-Certificate -Cert $cert -FilePath $cerFile -Force | Out-Null
    Import-Certificate -FilePath $cerFile -CertStoreLocation $store | Out-Null
    Write-Output "已导入信任: $store"
}

# 3) 签名
$signature = Set-AuthenticodeSignature -FilePath $Exe -Certificate $cert -HashAlgorithm SHA256
Write-Output "签名状态: $($signature.Status)"

$check = Get-AuthenticodeSignature $Exe
Write-Output "复核: Status=$($check.Status)  签名者=$($check.SignerCertificate.Subject)"
if ($check.Status -ne 'Valid') {
    throw "签名未生效: $($check.StatusMessage)"
}

Write-Output "完成：$Exe 已带本机受信任的签名"
