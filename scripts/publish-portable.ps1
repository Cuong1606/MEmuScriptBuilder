[CmdletBinding()]
param(
    [AllowEmptyString()]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-ValidReleaseVersion {
    param([AllowEmptyString()][string]$Value)

    if ($Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw "Version must use numeric major.minor.patch format without leading zeroes (for example, 1.0.0)."
    }
}

function Assert-RepositoryRoot {
    param([string]$RepositoryRoot)

    $requiredPaths = @(
        "MEmuScriptStudio.sln",
        "src\MEmuScriptStudio.App\MEmuScriptStudio.App.csproj",
        "assets\branding\AppIcon.png",
        "src\MEmuScriptStudio.App\Assets\AppIcon.ico",
        "HUONG-DAN-SU-DUNG.md",
        "tools\adb\adb.exe",
        "tools\adb\AdbWinApi.dll",
        "tools\adb\AdbWinUsbApi.dll",
        "tools\adb\LICENSE.txt",
        "tools\adb\NOTICE.txt"
    )
    foreach ($relativePath in $requiredPaths) {
        $path = Join-Path $RepositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Repository validation failed; required file is missing: $path"
        }
    }

    $gitRootOutput = @(& git -C $RepositoryRoot rev-parse --show-toplevel 2>&1)
    if ($LASTEXITCODE -ne 0 -or $gitRootOutput.Count -eq 0) {
        throw "Repository validation failed; git could not resolve the repository root. $($gitRootOutput -join ' ')"
    }

    $resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
    $resolvedGitRoot = [IO.Path]::GetFullPath([string]$gitRootOutput[-1]).TrimEnd('\', '/')
    if (-not $resolvedRepositoryRoot.Equals($resolvedGitRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Repository validation failed; script root '$resolvedRepositoryRoot' differs from git root '$resolvedGitRoot'."
    }
}

function Assert-DirectChildPath {
    param(
        [string]$CandidatePath,
        [string]$ExpectedParent
    )

    $resolvedCandidate = [IO.Path]::GetFullPath($CandidatePath).TrimEnd('\', '/')
    $resolvedParent = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd('\', '/')
    $candidateParent = [IO.Path]::GetDirectoryName($resolvedCandidate).TrimEnd('\', '/')
    if (-not $candidateParent.Equals($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe release path '$resolvedCandidate'; expected a direct child of '$resolvedParent'."
    }

    return $resolvedCandidate
}

function Assert-NoReparsePointsBelowRoot {
    param(
        [string]$CandidatePath,
        [string]$TrustedRoot
    )

    $resolvedCandidate = [IO.Path]::GetFullPath($CandidatePath).TrimEnd('\', '/')
    $resolvedTrustedRoot = [IO.Path]::GetFullPath($TrustedRoot).TrimEnd('\', '/')
    $trustedPrefix = $resolvedTrustedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCandidate.StartsWith($trustedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe path '$resolvedCandidate'; expected a descendant of trusted root '$resolvedTrustedRoot'."
    }

    $currentPath = $resolvedCandidate
    while (-not $currentPath.Equals($resolvedTrustedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $currentPath) {
            $currentItem = Get-Item -LiteralPath $currentPath -Force
            if (($currentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing release cleanup because the path chain contains a reparse point: $currentPath"
            }
        }
        $currentPath = [IO.Path]::GetDirectoryName($currentPath).TrimEnd('\', '/')
    }
}

function Remove-SafeReleaseOutput {
    param(
        [string]$CandidatePath,
        [string]$ExpectedParent,
        [string]$TrustedRoot
    )

    Assert-NoReparsePointsBelowRoot -CandidatePath $ExpectedParent -TrustedRoot $TrustedRoot
    $resolvedCandidate = Assert-DirectChildPath -CandidatePath $CandidatePath -ExpectedParent $ExpectedParent
    if (-not (Test-Path -LiteralPath $resolvedCandidate)) {
        return
    }

    $item = Get-Item -LiteralPath $resolvedCandidate -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove release output because it is a reparse point: $resolvedCandidate"
    }
    if ($item.PSIsContainer) {
        $nestedReparsePoint = Get-ChildItem -LiteralPath $resolvedCandidate -Force -Recurse |
            Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
            Select-Object -First 1
        if ($null -ne $nestedReparsePoint) {
            throw "Refusing to remove release output because it contains a reparse point: $($nestedReparsePoint.FullName)"
        }
    }

    Remove-Item -LiteralPath $resolvedCandidate -Recurse -Force
}

function Assert-SourcePng {
    param([string]$PngPath)

    Add-Type -AssemblyName System.Drawing
    $image = [Drawing.Image]::FromFile($PngPath)
    try {
        if ($image.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) {
            throw "Branding source is not a valid PNG: $PngPath"
        }
        if ($image.Width -ne $image.Height) {
            throw "Branding source must be square; found $($image.Width)x$($image.Height)."
        }
        if ($image.Width -lt 256) {
            throw "Branding source must be at least 256x256; found $($image.Width)x$($image.Height)."
        }
        if ($image.Width -ne 1024) {
            Write-Warning "The recommended branding source size is 1024x1024; found $($image.Width)x$($image.Height)."
        }
    }
    finally {
        $image.Dispose()
    }
}

function Get-IconSizes {
    param([string]$IconPath)

    $stream = [IO.File]::OpenRead($IconPath)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $reserved = $reader.ReadUInt16()
        $type = $reader.ReadUInt16()
        $count = $reader.ReadUInt16()
        if ($reserved -ne 0 -or $type -ne 1 -or $count -lt 1) {
            throw "Invalid ICO header: $IconPath"
        }

        $sizes = [Collections.Generic.List[int]]::new()
        for ($index = 0; $index -lt $count; $index++) {
            $widthByte = $reader.ReadByte()
            $heightByte = $reader.ReadByte()
            [void]$reader.ReadByte()
            [void]$reader.ReadByte()
            [void]$reader.ReadUInt16()
            [void]$reader.ReadUInt16()
            [void]$reader.ReadUInt32()
            [void]$reader.ReadUInt32()
            $width = if ($widthByte -eq 0) { 256 } else { [int]$widthByte }
            $height = if ($heightByte -eq 0) { 256 } else { [int]$heightByte }
            if ($width -ne $height) {
                throw "ICO contains a non-square frame: ${width}x${height}."
            }
            $sizes.Add($width)
        }
        return @($sizes)
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Assert-ApplicationIcon {
    param([string]$IconPath)

    $actualSizes = @(Get-IconSizes -IconPath $IconPath)
    foreach ($requiredSize in @(16, 32, 48, 256)) {
        if ($actualSizes -notcontains $requiredSize) {
            throw "Application icon is missing the ${requiredSize}x${requiredSize} frame: $IconPath"
        }
    }
}

function Assert-PortableContents {
    param([string]$PortableDirectory)

    $requiredRelativeFiles = @(
        "MEmuScriptStudio.exe",
        "README.txt",
        "Create Desktop Shortcut.cmd",
        "HUONG-DAN-SU-DUNG.md",
        "tools\adb\adb.exe",
        "tools\adb\AdbWinApi.dll",
        "tools\adb\AdbWinUsbApi.dll",
        "tools\adb\LICENSE.txt",
        "tools\adb\NOTICE.txt"
    )
    foreach ($relativePath in $requiredRelativeFiles) {
        $requiredPath = Join-Path $PortableDirectory $relativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Portable output is missing required file: $relativePath"
        }
        if ((Get-Item -LiteralPath $requiredPath).Length -le 0) {
            throw "Portable required file is empty: $relativePath"
        }
    }

    $allowedAdbFileNames = @("adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "LICENSE.txt", "NOTICE.txt")
    $adbDirectory = Join-Path $PortableDirectory "tools\adb"
    $nestedAdbDirectories = @(Get-ChildItem -LiteralPath $adbDirectory -Directory -Recurse)
    if ($nestedAdbDirectories.Count -gt 0) {
        throw "Portable ADB bundle contains an unexpected directory: $($nestedAdbDirectories[0].FullName)"
    }
    $unexpectedAdbFiles = @(Get-ChildItem -LiteralPath $adbDirectory -File -Recurse | Where-Object {
        $allowedAdbFileNames -notcontains $_.Name
    })
    if ($unexpectedAdbFiles.Count -gt 0) {
        throw "Portable ADB bundle contains an unexpected file: $($unexpectedAdbFiles[0].FullName)"
    }

    $forbiddenDirectoryNames = @(
        ".git", "bin", "obj", "tests", "TestResults", "logs",
        "platform-tools", "build-tools", "platforms", "cmdline-tools", "emulator"
    )
    $forbiddenDirectories = @(Get-ChildItem -LiteralPath $PortableDirectory -Directory -Recurse | Where-Object {
        $forbiddenDirectoryNames -contains $_.Name
    })
    if ($forbiddenDirectories.Count -gt 0) {
        throw "Portable output contains a forbidden directory: $($forbiddenDirectories[0].FullName)"
    }

    $forbiddenExtensions = @(".cs", ".csproj", ".sln", ".pdb", ".log", ".user", ".suo")
    $forbiddenFileNames = @("settings.json", "scripts.json", ".env", ".env.local")
    $forbiddenFiles = @(Get-ChildItem -LiteralPath $PortableDirectory -File -Recurse | Where-Object {
        $forbiddenExtensions -contains $_.Extension -or $forbiddenFileNames -contains $_.Name
    })
    if ($forbiddenFiles.Count -gt 0) {
        throw "Portable output contains a forbidden file: $($forbiddenFiles[0].FullName)"
    }
}

if ($MyInvocation.InvocationName -eq ".") {
    return
}

$stagingDirectory = $null
try {
    Assert-ValidReleaseVersion -Value $Version

    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    Assert-RepositoryRoot -RepositoryRoot $repositoryRoot

    $appProject = Join-Path $repositoryRoot "src\MEmuScriptStudio.App\MEmuScriptStudio.App.csproj"
    $sourcePng = Join-Path $repositoryRoot "assets\branding\AppIcon.png"
    $applicationIcon = Join-Path $repositoryRoot "src\MEmuScriptStudio.App\Assets\AppIcon.ico"
    $portableReadme = Join-Path $repositoryRoot "assets\portable\README.txt"
    $shortcutScript = Join-Path $repositoryRoot "assets\portable\Create Desktop Shortcut.cmd"
    Assert-SourcePng -PngPath $sourcePng
    Assert-ApplicationIcon -IconPath $applicationIcon
    foreach ($requiredPortableFile in @($portableReadme, $shortcutScript)) {
        if (-not (Test-Path -LiteralPath $requiredPortableFile -PathType Leaf)) {
            throw "Portable release asset is missing: $requiredPortableFile"
        }
    }

    $portableRoot = Join-Path $repositoryRoot "artifacts\portable"
    [IO.Directory]::CreateDirectory($portableRoot) | Out-Null
    Assert-NoReparsePointsBelowRoot -CandidatePath $portableRoot -TrustedRoot $repositoryRoot

    $releaseName = "MEmuScriptStudio-$Version-win-x64"
    $releaseDirectory = Assert-DirectChildPath -CandidatePath (Join-Path $portableRoot $releaseName) -ExpectedParent $portableRoot
    $zipName = "MEmuScriptStudio-Portable-$Version-win-x64.zip"
    $zipPath = Assert-DirectChildPath -CandidatePath (Join-Path $portableRoot $zipName) -ExpectedParent $portableRoot
    $checksumPath = Assert-DirectChildPath -CandidatePath "$zipPath.sha256" -ExpectedParent $portableRoot
    $stagingName = ".staging-$releaseName-$([Guid]::NewGuid().ToString('N'))"
    $stagingDirectory = Assert-DirectChildPath -CandidatePath (Join-Path $portableRoot $stagingName) -ExpectedParent $portableRoot

    foreach ($oldOutput in @($releaseDirectory, $zipPath, $checksumPath)) {
        Remove-SafeReleaseOutput -CandidatePath $oldOutput -ExpectedParent $portableRoot -TrustedRoot $repositoryRoot
    }
    [IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null

    Write-Output "Publishing MEmu Script Studio $Version for win-x64..."
    $publishArguments = @(
        "publish",
        $appProject,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishProfile=PortableWinX64",
        "-p:PublishSingleFile=false",
        "-p:PublishTrimmed=false",
        "-p:Version=$Version",
        "-p:FileVersion=$Version",
        "-p:InformationalVersion=$Version",
        "--output", $stagingDirectory,
        "--nologo"
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    Get-ChildItem -LiteralPath $stagingDirectory -File -Recurse |
        Where-Object { $_.Extension -ieq ".pdb" } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

    Copy-Item -LiteralPath $portableReadme -Destination (Join-Path $stagingDirectory "README.txt")
    Copy-Item -LiteralPath $shortcutScript -Destination (Join-Path $stagingDirectory "Create Desktop Shortcut.cmd")

    $publishedExecutable = Join-Path $stagingDirectory "MEmuScriptStudio.exe"
    if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
        throw "Published executable was not created: $publishedExecutable"
    }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($publishedExecutable)
    if ($versionInfo.ProductName -ne "MEmu Script Studio") {
        throw "Published Product metadata is incorrect: '$($versionInfo.ProductName)'."
    }
    if ($versionInfo.ProductVersion -ne $Version) {
        throw "Published ProductVersion is incorrect: '$($versionInfo.ProductVersion)'."
    }
    if ($versionInfo.FileVersion -ne $Version -and $versionInfo.FileVersion -ne "$Version.0") {
        throw "Published FileVersion is incorrect: '$($versionInfo.FileVersion)'."
    }

    Assert-PortableContents -PortableDirectory $stagingDirectory
    Move-Item -LiteralPath $stagingDirectory -Destination $releaseDirectory
    $stagingDirectory = $null

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $releaseDirectory,
        $zipPath,
        [IO.Compression.CompressionLevel]::Optimal,
        $true)

    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$zipHash *$zipName" | Set-Content -LiteralPath $checksumPath -Encoding Ascii

    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $archiveFileEntries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith("/", [StringComparison]::Ordinal) })
        $archiveNames = @($archiveFileEntries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $archivePrefix = "$releaseName/"
        foreach ($requiredEntry in @(
            "${archivePrefix}MEmuScriptStudio.exe",
            "${archivePrefix}README.txt",
            "${archivePrefix}Create Desktop Shortcut.cmd",
            "${archivePrefix}HUONG-DAN-SU-DUNG.md",
            "${archivePrefix}tools/adb/adb.exe",
            "${archivePrefix}tools/adb/AdbWinApi.dll",
            "${archivePrefix}tools/adb/AdbWinUsbApi.dll",
            "${archivePrefix}tools/adb/LICENSE.txt",
            "${archivePrefix}tools/adb/NOTICE.txt"
        )) {
            if ($archiveNames -notcontains $requiredEntry) {
                throw "ZIP is missing required entry: $requiredEntry"
            }
        }
        if (@($archiveFileEntries | Where-Object { $_.Name.EndsWith(".pdb", [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            throw "ZIP contains developer PDB files."
        }
        $fileCount = $archiveFileEntries.Count
    }
    finally {
        $archive.Dispose()
    }

    $zipLength = (Get-Item -LiteralPath $zipPath).Length
    $zipSizeMiB = [Math]::Round($zipLength / 1MB, 2)
    Write-Output "PortableDirectory=$releaseDirectory"
    Write-Output "ZipPath=$zipPath"
    Write-Output "ChecksumPath=$checksumPath"
    Write-Output "SHA256=$zipHash"
    Write-Output "ZipSizeBytes=$zipLength"
    Write-Output "ZipSizeMiB=$zipSizeMiB"
    Write-Output "FileCount=$fileCount"
}
catch {
    Write-Error "Portable publish failed: $($_.Exception.Message)"
    exit 1
}
finally {
    if ($null -ne $stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory)) {
        try {
            $portableRootForCleanup = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))) "artifacts\portable"
            Remove-SafeReleaseOutput -CandidatePath $stagingDirectory -ExpectedParent $portableRootForCleanup -TrustedRoot ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..")))
        }
        catch {
            Write-Warning "Staging cleanup was skipped for safety: $($_.Exception.Message)"
        }
    }
}
