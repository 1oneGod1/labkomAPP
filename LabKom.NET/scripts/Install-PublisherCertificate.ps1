#Requires -RunAsAdministrator
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$CertificatePath,

    [Parameter(Mandatory)]
    [switch]$IUnderstandSelfSignedTrustRisk
)

$ErrorActionPreference = 'Stop'
$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
$ekuExtension = $certificate.Extensions |
    Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
    Select-Object -First 1
$hasCodeSigningUsage = $false
if ($ekuExtension) {
    $enhancedKeyUsage = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ekuExtension
    $hasCodeSigningUsage = @($enhancedKeyUsage.EnhancedKeyUsages |
        Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }
    ).Count -gt 0
}
if (-not $hasCodeSigningUsage) {
    throw 'Sertifikat tidak memiliki Extended Key Usage Code Signing.'
}

$description = "Mempercayai publisher $($certificate.Subject) thumbprint $($certificate.Thumbprint)"
if ($PSCmdlet.ShouldProcess($env:COMPUTERNAME, $description)) {
    Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null
    Write-Host 'Publisher LabKom dipercaya pada komputer ini.' -ForegroundColor Green
    Write-Warning 'Hanya distribusikan public CER. Jangan pernah menyalin PFX ke PC siswa.'
}
