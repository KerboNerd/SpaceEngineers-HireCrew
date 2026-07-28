#Requires -Version 5.1
<#
.SYNOPSIS
  Stage a clean HireCrew mod payload and publish/update it via SteamCMD.

.DESCRIPTION
  Copies only Data/, Models/, Textures/, metadata.mod into workshop/content,
  generates workshop/preview.jpg if missing, writes workshop/hirecrew.vdf,
  ensures SteamCMD is present, then runs interactive login + workshop_build_item.

  First run creates a NEW private Workshop item (publishedfileid 0).
  SteamCMD rewrites the VDF with the new id for later updates.

.PARAMETER ChangeNote
  Workshop changenote. Default: Initial release when unpublished, else a timestamp.

.PARAMETER Title
  Workshop title.

.PARAMETER Description
  Workshop description (plain text).

.PARAMETER SkipUpload
  Stage content, generate preview/VDF. Do not publish.

.PARAMETER ForcePreview
  Regenerate preview.jpg even if it already exists.

.PARAMETER NoPreview
  Omit previewfile from the VDF. Use when Steam returns Limit exceeded on preview
  (SE Steam Cloud file-count / rate limit). Content-only update still works.
#>
[CmdletBinding()]
param(
    [string] $ChangeNote = '',
    [string] $Title = 'HireCrew',
    [string] $Description = 'Hire NPC crew for your ships. Assign roles, manage morale, and run your grid with hired help.',
    [switch] $SkipUpload,
    [switch] $ForcePreview,
    [switch] $NoPreview
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WorkshopDir = $PSScriptRoot
$ModRoot = Split-Path -Parent $WorkshopDir
$ContentDir = Join-Path $WorkshopDir 'content'
$PreviewPath = Join-Path $WorkshopDir 'preview.jpg'
$VdfPath = Join-Path $WorkshopDir 'hirecrew.vdf'
$SteamCmdDir = Join-Path $WorkshopDir 'steamcmd'
$SteamCmdExe = Join-Path $SteamCmdDir 'steamcmd.exe'
$SteamCmdZipUrl = 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip'
$AppId = '244850'
# 0 public, 1 friends-only, 2 private
$Visibility = '2'

function Write-Info([string] $Message) {
    Write-Host "[HireCrew] $Message"
}

function Ensure-SteamCmd {
    if (Test-Path -LiteralPath $SteamCmdExe) {
        Write-Info "SteamCMD found: $SteamCmdExe"
        return
    }

    Write-Info 'Downloading SteamCMD...'
    New-Item -ItemType Directory -Force -Path $SteamCmdDir | Out-Null
    $zipPath = Join-Path $SteamCmdDir 'steamcmd.zip'
    Invoke-WebRequest -Uri $SteamCmdZipUrl -OutFile $zipPath
    Expand-Archive -LiteralPath $zipPath -DestinationPath $SteamCmdDir -Force
    Remove-Item -LiteralPath $zipPath -Force

    if (-not (Test-Path -LiteralPath $SteamCmdExe)) {
        throw "SteamCMD download finished but steamcmd.exe was not found at $SteamCmdExe"
    }

    Write-Info 'Running SteamCMD once to self-update (this can take a minute)...'
    & $SteamCmdExe +quit | Out-Host
}

function Clear-ContentDir {
    if (Test-Path -LiteralPath $ContentDir) {
        Remove-Item -LiteralPath $ContentDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $ContentDir | Out-Null
}

function Copy-ModPayload {
    Write-Info "Staging clean content into $ContentDir"

    foreach ($name in @('Data', 'Models', 'Textures')) {
        $src = Join-Path $ModRoot $name
        if (-not (Test-Path -LiteralPath $src)) {
            throw "Missing required folder: $src"
        }
        $dst = Join-Path $ContentDir $name
        Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
    }

    $metaSrc = Join-Path $ModRoot 'metadata.mod'
    if (-not (Test-Path -LiteralPath $metaSrc)) {
        throw "Missing metadata.mod at $metaSrc"
    }
    Copy-Item -LiteralPath $metaSrc -Destination (Join-Path $ContentDir 'metadata.mod') -Force

    $junkExt = @('.fbx', '.blend', '.pdb', '.dll', '.csproj', '.sln', '.log')
    Get-ChildItem -LiteralPath $ContentDir -Recurse -File |
        Where-Object { $junkExt -contains $_.Extension.ToLowerInvariant() } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $fileCount = @(Get-ChildItem -LiteralPath $ContentDir -Recurse -File).Count
    if ($fileCount -eq 0) {
        throw "Staging produced 0 files under $ContentDir"
    }
    Write-Info "Staged $fileCount files"
}

function New-PreviewImage {
    if ((Test-Path -LiteralPath $PreviewPath) -and -not $ForcePreview) {
        Write-Info "Using existing preview: $PreviewPath"
        return
    }

    Write-Info 'Generating preview.jpg (512x512)'
    Add-Type -AssemblyName System.Drawing

    $size = 512
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

        $bg = [System.Drawing.Color]::FromArgb(255, 18, 28, 38)
        $g.Clear($bg)

        $accentBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 70, 140, 180))
        $g.FillRectangle($accentBrush, 0, 420, $size, 92)
        $accentBrush.Dispose()

        $titleFont = New-Object System.Drawing.Font 'Segoe UI', 54, ([System.Drawing.FontStyle]::Bold)
        $subFont = New-Object System.Drawing.Font 'Segoe UI', 18, ([System.Drawing.FontStyle]::Regular)
        $titleBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 235, 242, 248))
        $subBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 28, 38))
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Center

        $titleRect = New-Object System.Drawing.RectangleF 0, 140, $size, 120
        $subRect = New-Object System.Drawing.RectangleF 0, 420, $size, 92
        $g.DrawString('HireCrew', $titleFont, $titleBrush, $titleRect, $sf)
        $g.DrawString('NPC crew for your ships', $subFont, $subBrush, $subRect, $sf)

        $titleFont.Dispose()
        $subFont.Dispose()
        $titleBrush.Dispose()
        $subBrush.Dispose()
        $sf.Dispose()

        $jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
            Where-Object { $_.MimeType -eq 'image/jpeg' } |
            Select-Object -First 1
        $encoder = [System.Drawing.Imaging.Encoder]::Quality
        $encoderParams = New-Object System.Drawing.Imaging.EncoderParameters 1
        $encoderParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter $encoder, 90L
        $bmp.Save($PreviewPath, $jpegCodec, $encoderParams)
        $encoderParams.Dispose()
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }

    $len = (Get-Item -LiteralPath $PreviewPath).Length
    if ($len -ge 1MB) {
        throw "preview.jpg is $len bytes (Steam limit is 1MB). Regenerate smaller or replace manually."
    }
}

function Get-PublishedFileIdFromVdf {
    if (-not (Test-Path -LiteralPath $VdfPath)) {
        return '0'
    }
    $text = Get-Content -LiteralPath $VdfPath -Raw
    if ($text -match '"publishedfileid"\s+"(\d+)"') {
        return $Matches[1]
    }
    return '0'
}

function Escape-VdfPath([string] $Path) {
    $bs = [string][char]92
    return $Path.Replace($bs, ($bs + $bs))
}

function Write-WorkshopVdf {
    $publishedId = Get-PublishedFileIdFromVdf
    $note = $ChangeNote
    if ([string]::IsNullOrWhiteSpace($note)) {
        if ($publishedId -eq '0') {
            $note = 'Initial release'
        }
        else {
            $note = 'Update ' + (Get-Date -Format 'yyyy-MM-dd HH:mm')
        }
    }

    $contentEsc = Escape-VdfPath $ContentDir
    $descEsc = ($Description -replace '"', "'")

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('"workshopitem"') | Out-Null
    $lines.Add('{') | Out-Null
    $lines.Add("`t`"appid`"`t`t`"$AppId`"") | Out-Null
    $lines.Add("`t`"publishedfileid`"`t`t`"$publishedId`"") | Out-Null
    $lines.Add("`t`"contentfolder`"`t`t`"$contentEsc`"") | Out-Null
    if (-not $NoPreview) {
        $previewEsc = Escape-VdfPath $PreviewPath
        $lines.Add("`t`"previewfile`"`t`t`"$previewEsc`"") | Out-Null
    }
    $lines.Add("`t`"visibility`"`t`t`"$Visibility`"") | Out-Null
    $lines.Add("`t`"title`"`t`t`"$Title`"") | Out-Null
    $lines.Add("`t`"description`"`t`t`"$descEsc`"") | Out-Null
    $lines.Add("`t`"changenote`"`t`t`"$note`"") | Out-Null
    $lines.Add('}') | Out-Null
    Set-Content -LiteralPath $VdfPath -Value $lines.ToArray() -Encoding ASCII

    if ($publishedId -eq '0') {
        Write-Info "Wrote VDF for NEW private item: $VdfPath"
    }
    else {
        Write-Info "Wrote VDF to update item $publishedId : $VdfPath"
    }
    if ($NoPreview) {
        Write-Info 'NoPreview: VDF has no previewfile (content/metadata only).'
    }
}

function Invoke-WorkshopUpload {
    Write-Info 'Starting SteamCMD (interactive login). Steam Guard code may be required.'
    Write-Info 'Steam client may log out while SteamCMD is signed in.'

    $steamUser = Read-Host 'Steam username'
    if ([string]::IsNullOrWhiteSpace($steamUser)) {
        throw 'Steam username is required'
    }

    & $SteamCmdExe +login $steamUser +workshop_build_item $VdfPath +quit
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        $hint = @"
SteamCMD exited with code $code.

If you saw 'Limit exceeded' while Uploading preview image:
  - Your item id is already in hirecrew.vdf (keep it; do not create another).
  - Retry content-only:
      .\workshop\Publish-HireCrew.ps1 -NoPreview
  - Preview is not oversized (ours is ~16KB). This is usually SE Steam Cloud
    file-count / rate limit. Free space:
      https://store.steampowered.com/account/remotestorage
    Delete cloud blueprints under %AppData%\SpaceEngineers\Blueprints\cloud
    then retry preview later without -NoPreview.
"@
        throw $hint
    }

    $newId = Get-PublishedFileIdFromVdf
    if ($newId -eq '0') {
        Write-Warning 'Upload finished but publishedfileid is still 0. Check SteamCMD output above.'
    }
    else {
        Write-Info "Workshop item id: $newId"
        Write-Info "https://steamcommunity.com/sharedfiles/filedetails/?id=$newId"
    }
}

Clear-ContentDir
Copy-ModPayload
if (-not $NoPreview) {
    New-PreviewImage
}
Write-WorkshopVdf

if ($SkipUpload) {
    Write-Info 'SkipUpload set - staging complete. Run without -SkipUpload to publish.'
    exit 0
}

Ensure-SteamCmd
Invoke-WorkshopUpload
Write-Info 'Done.'
