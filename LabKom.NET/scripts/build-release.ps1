#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$PfxPath,

    [string]$PfxPasswordEnvironmentVariable = 'LABKOM_SIGNING_PASSWORD',

    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = '1oneGod1/labkomAPP',

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\release'),

    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-LastExitCode([string]$Action) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Action gagal dengan exit code $LASTEXITCODE."
    }
}

function Find-SignTool {
    $kitsRoot = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10\bin'
    $tool = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $tool) {
        throw 'signtool.exe x64 tidak ditemukan. Pasang Windows 10/11 SDK.'
    }
    return $tool.FullName
}

function Sign-File([string]$Path, [string]$SignTool, [string]$Thumbprint) {
    & $SignTool sign /sha1 $Thumbprint /fd SHA256 /td SHA256 /tr $TimestampServer $Path
    Assert-LastExitCode "Signing $Path"
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if (-not $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $Thumbprint) {
        throw "Verifikasi Authenticode gagal: $Path"
    }
}

function Publish-SingleFile([string]$Project, [string]$Destination, [hashtable]$ExtraProperties) {
    $arguments = @(
        'publish', $Project,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $Destination,
        '-p:PublishSingleFile=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:Version=$Version"
    )
    foreach ($entry in $ExtraProperties.GetEnumerator()) {
        $arguments += "-p:$($entry.Key)=$($entry.Value)"
    }
    & dotnet @arguments
    Assert-LastExitCode "Publish $Project"
}

function Write-ReleaseDescriptor([string]$Directory, [string]$Component) {
    [ordered]@{
        schemaVersion = 1
        component = $Component
        version = $Version
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Directory 'release.json') -Encoding utf8NoBOM
}

function Write-SignedUpdateManifest(
    [string]$Component,
    [string]$PackagePath,
    [string]$OutputPath,
    [Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
) {
    $tag = "native-v$Version"
    $asset = [IO.Path]::GetFileName($PackagePath)
    $packageUrl = "https://github.com/$Repository/releases/download/$tag/$asset"
    $published = [DateTimeOffset]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    $sha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $canonical = [string]::Join([char]10, @('1', $Component, $Version, $published, $packageUrl, $sha256))

    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
    if (-not $rsa) { throw 'Private key RSA signing tidak tersedia.' }
    try {
        $signatureBytes = $rsa.SignData(
            [Text.Encoding]::UTF8.GetBytes($canonical),
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pss)
    }
    finally {
        $rsa.Dispose()
    }

    [ordered]@{
        schemaVersion = 1
        component = $Component
        version = $Version
        publishedAtUtc = $published
        packageUrl = $packageUrl
        sha256 = $sha256
        signature = [Convert]::ToBase64String($signatureBytes)
    } | ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.StartsWith(
    $repoRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory harus berada di dalam folder LabKom.NET.'
}
$PfxPath = [IO.Path]::GetFullPath($PfxPath)
$passwordText = [Environment]::GetEnvironmentVariable($PfxPasswordEnvironmentVariable)
if ([string]::IsNullOrEmpty($passwordText)) {
    throw "Environment variable $PfxPasswordEnvironmentVariable belum diisi."
}

$securePassword = ConvertTo-SecureString $passwordText -AsPlainText -Force
$existingCertificate = Get-ChildItem 'Cert:\CurrentUser\My' |
    Where-Object { $_.HasPrivateKey } |
    ForEach-Object { $_ }
$importArguments = @{
    FilePath = $PfxPath
    Password = $securePassword
    CertStoreLocation = 'Cert:\CurrentUser\My'
    Exportable = $true
}
$importedCertificates = @(Import-PfxCertificate @importArguments)
$certificate = $importedCertificates |
    Where-Object { $_.HasPrivateKey } |
    Select-Object -First 1
if (-not $certificate) { throw 'PFX tidak memiliki private key code-signing.' }
$wasAlreadyInstalled = @($existingCertificate |
    Where-Object { $_.Thumbprint -eq $certificate.Thumbprint }).Count -gt 0

$work = Join-Path $repoRoot 'artifacts\work'
if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory, $work -Force | Out-Null

Push-Location $repoRoot
try {
    $signTool = Find-SignTool
    $publicCertificate = Join-Path $work 'update-public.cer'
    Export-Certificate -Cert $certificate -FilePath $publicCertificate -Type CERT | Out-Null

    $studentComponent = Join-Path $work 'component-student'
    $teacherComponent = Join-Path $work 'component-teacher'
    $updaterPublish = Join-Path $work 'updater'
    $provisioningPublish = Join-Path $work 'provisioning'

    Publish-SingleFile 'src\LabKom.Student\LabKom.Student.csproj' (Join-Path $studentComponent 'Agent') @{}
    Publish-SingleFile 'src\LabKom.Student.Desktop\LabKom.Student.Desktop.csproj' (Join-Path $studentComponent 'Desktop') @{}
    Publish-SingleFile 'src\LabKom.Teacher\LabKom.Teacher.csproj' $teacherComponent @{}
    Publish-SingleFile 'src\LabKom.Provisioning\LabKom.Provisioning.csproj' $provisioningPublish @{}
    Publish-SingleFile 'src\LabKom.Updater\LabKom.Updater.csproj' $updaterPublish @{}

    $studentAdmin = Join-Path $studentComponent 'Admin'
    New-Item -ItemType Directory -Path $studentAdmin -Force | Out-Null
    Copy-Item -Path (Join-Path $provisioningPublish '*') -Destination $studentAdmin -Recurse -Force
    Copy-Item -Path (Join-Path $provisioningPublish '*') -Destination $teacherComponent -Recurse -Force
    Write-ReleaseDescriptor $studentComponent 'Student'
    Write-ReleaseDescriptor $teacherComponent 'Teacher'

    Get-ChildItem $studentComponent, $teacherComponent, $updaterPublish -Filter *.exe -Recurse |
        ForEach-Object { Sign-File $_.FullName $signTool $certificate.Thumbprint }

    $studentPackage = Join-Path $OutputDirectory "LabKom-Student-$Version.zip"
    $teacherPackage = Join-Path $OutputDirectory "LabKom-Teacher-$Version.zip"
    Compress-Archive -Path (Join-Path $studentComponent '*') -DestinationPath $studentPackage -CompressionLevel Optimal
    Compress-Archive -Path (Join-Path $teacherComponent '*') -DestinationPath $teacherPackage -CompressionLevel Optimal

    Write-SignedUpdateManifest 'Student' $studentPackage (Join-Path $OutputDirectory 'LabKom-Student-update.json') $certificate
    Write-SignedUpdateManifest 'Teacher' $teacherPackage (Join-Path $OutputDirectory 'LabKom-Teacher-update.json') $certificate

    foreach ($component in @('Student', 'Teacher')) {
        $payloadRoot = Join-Path $work "installer-payload-$($component.ToLowerInvariant())"
        $componentPayload = Join-Path $payloadRoot 'component'
        $updaterPayload = Join-Path $payloadRoot 'updater'
        New-Item -ItemType Directory -Path $componentPayload, $updaterPayload -Force | Out-Null
        $sourceComponent = if ($component -eq 'Student') { $studentComponent } else { $teacherComponent }
        Copy-Item -Path (Join-Path $sourceComponent '*') -Destination $componentPayload -Recurse -Force
        Copy-Item -Path (Join-Path $updaterPublish '*') -Destination $updaterPayload -Recurse -Force
        Copy-Item -LiteralPath $publicCertificate -Destination (Join-Path $payloadRoot 'update-public.cer')

        $payloadZip = Join-Path $work "installer-payload-$($component.ToLowerInvariant()).zip"
        Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadZip -CompressionLevel Optimal
        $setupPublish = Join-Path $work "setup-$($component.ToLowerInvariant())"
        Publish-SingleFile 'src\LabKom.Installer\LabKom.Installer.csproj' $setupPublish @{
            InstallerComponent = $component
            PayloadPath = $payloadZip
            SetupAssemblyName = "LabKom-$component-Setup"
        }
        $setupSource = Join-Path $setupPublish "LabKom-$component-Setup.exe"
        Sign-File $setupSource $signTool $certificate.Thumbprint
        Copy-Item -LiteralPath $setupSource -Destination (Join-Path $OutputDirectory "LabKom-$component-Setup-$Version.exe")
    }

    Export-Certificate -Cert $certificate -FilePath (Join-Path $OutputDirectory 'LabKom-Publisher.cer') -Type CERT | Out-Null

    Get-ChildItem $OutputDirectory -File |
        Sort-Object Name |
        ForEach-Object {
            "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
        } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii

    Write-Host "Release LabKom $Version selesai: $OutputDirectory" -ForegroundColor Green
}
finally {
    Pop-Location
    if (-not $wasAlreadyInstalled -and $certificate) {
        Remove-Item -LiteralPath $certificate.PSPath -Force -ErrorAction SilentlyContinue
    }
    $passwordText = $null
    $securePassword = $null
}
