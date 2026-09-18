param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$manifest = Get-Content (Join-Path $root 'assets/branding/manifest.json') -Raw | ConvertFrom-Json
foreach ($item in $manifest) {
    $path = Join-Path $root $item.path
    Require (Test-Path -LiteralPath $path) "Missing: $($item.path)"
    Require ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $item.sha256) "Stale: $($item.path)"
}
$iosRoot = Join-Path $root 'src/Unlimotion.iOS/Assets.xcassets/AppIcon.appiconset'
[xml]$plist = Get-Content (Join-Path $root 'src/Unlimotion.iOS/Info.plist')
$iconSet = $plist.SelectSingleNode("/plist/dict/key[text()='XSAppIconAssets']/following-sibling::string[1]")
Require ($null -ne $iconSet -and $iconSet.InnerText -eq 'Assets.xcassets/AppIcon.appiconset') 'iOS Info.plist must select AppIcon'
$ios = Get-Content (Join-Path $iosRoot 'Contents.json') -Raw | ConvertFrom-Json
foreach ($item in $ios.images) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $iosRoot $item.filename))
    Require ($bytes[25] -eq 2) "iOS icon has alpha: $($item.filename)"
    $size = [int]([double]::Parse(($item.size -split 'x')[0], [Globalization.CultureInfo]::InvariantCulture) * [int]($item.scale -replace 'x',''))
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    Require ($width -eq $size) "Wrong iOS slot: $($item.filename)"
}
foreach ($directory in @('wwwroot','AppBundle')) {
    $browserRoot = Join-Path $root "src/Unlimotion.Browser/$directory"
    $html = Get-Content (Join-Path $browserRoot 'index.html') -Raw
    foreach ($name in @('favicon.ico','icon-32.png','apple-touch-icon.png')) {
        Require ($html.Contains("./$name")) "Missing HTML link: $directory/$name"
        Require (Test-Path (Join-Path $browserRoot $name)) "Missing browser resource: $name"
    }
}
[xml]$android = Get-Content (Join-Path $root 'src/Unlimotion.Android/Properties/AndroidManifest.xml')
Require ($android.manifest.application.GetAttribute('icon','http://schemas.android.com/apk/res/android') -eq '@mipmap/ic_launcher') 'Android launcher reference is stale'
[xml]$adaptive = Get-Content (Join-Path $root 'src/Unlimotion.Android/Resources/mipmap-anydpi-v26/ic_launcher.xml')
Require ($adaptive.DocumentElement.Name -eq 'adaptive-icon') 'Missing adaptive icon root'
[xml]$debian = Get-Content (Join-Path $root 'src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj')
$linux = @($debian.Project.ItemGroup.Content | Where-Object { $_.Include -like 'Assets\hicolor*' })
Require ($linux.Count -eq 8) 'Incomplete Linux size matrix'
foreach ($item in $linux) {
    Require ($item.LinuxPath -match '^/usr/share/icons/hicolor/\d+x\d+/apps/unlimotion.png$') "Invalid LinuxPath: $($item.LinuxPath)"
    Require (Test-Path (Join-Path (Join-Path $root 'src/Unlimotion.Desktop') $item.Include)) "Missing Linux PNG: $($item.Include)"
}
foreach ($locale in @('en-US','ru-RU')) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $root "fastlane/metadata/android/$locale/images/icon.png"))
    Require ($bytes[25] -eq 2) "Store icon must be opaque: $locale"
    Require (($bytes[18] -eq 2) -and ($bytes[19] -eq 0)) 'Store icon must be 512px'
}
Write-Host "PASS: $($manifest.Count) resource hashes, iOS slots/opaque RGB, browser links, Android adaptive reference, Linux paths, store icons."
