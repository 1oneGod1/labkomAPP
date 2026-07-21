#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [SecureString]$PfxPassword,

    [ValidateNotNullOrEmpty()]
    [string]$Publisher = 'LabKom',

    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'LabKom-Signing'),

    [ValidateRange(1, 5)]
    [int]$ValidYears = 3
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) {
    throw 'Pembuatan sertifikat LabKom hanya didukung di Windows.'
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$certificateArguments = @{
    Type = 'CodeSigningCert'
    Subject = "CN=$Publisher"
    FriendlyName = 'LabKom Code Signing'
    CertStoreLocation = 'Cert:\CurrentUser\My'
    KeyAlgorithm = 'RSA'
    KeyLength = 3072
    HashAlgorithm = 'SHA256'
    KeyExportPolicy = 'Exportable'
    KeyUsage = 'DigitalSignature'
    NotAfter = (Get-Date).AddYears($ValidYears)
}
$certificate = New-SelfSignedCertificate @certificateArguments

$pfxPath = Join-Path $OutputDirectory 'LabKom-CodeSigning.pfx'
$cerPath = Join-Path $OutputDirectory 'LabKom-Publisher.cer'
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $PfxPassword -ChainOption EndEntityCertOnly | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT | Out-Null

Write-Host 'Sertifikat signing LabKom berhasil dibuat.' -ForegroundColor Green
Write-Host "PFX       : $pfxPath"
Write-Host "Public CER: $cerPath"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Warning 'PFX dan password adalah identitas penerbit. Simpan backup offline dan jangan commit ke Git.'
Write-Warning 'Sertifikat self-signed harus dipasang ke Trusted Root dan Trusted Publishers di setiap PC/GPO.'
