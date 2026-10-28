$ErrorActionPreference = "Stop"
$PSDefaultParameterValues["*:ErrorAction"] = "Stop"

$root = $PWD.Path

Write-Output "=== JoyNect repository readiness (honest, not heroic) ==="
Write-Output ""

if (-not (Test-Path ".git")) {
    Write-Output "There is no local git repository in this copy of the project."
    Write-Output ""
    Write-Output "If you want a local repo, create one here with `git init` and point it at your"
    Write-Output "own remote. Do not treat a freshly initialized local repo as anything more than"
    Write-Output "a placeholder until you have checked the real source of truth."
    Write-Output ""
    Write-Output "If you already published JoyNect somewhere (for example on GitHub under Scarneitor),"
    Write-Output "open that page in your browser first. Do not re-create the project from this"
    Write-Output "checkout as if it were the canonical repository."
    Write-Output ""
    Write-Output "Current local state:"
    Write-Output "  branches :"
    Write-Output "  remotes  :"
    Write-Output "  staged   :"
    Write-Output "  unstaged :"
    Write-Output "  untracked:"
    Write-Output ""
    Write-Output "n/a - no git repo present"
    exit 1
}

Write-Output "This directory already has a git repository. Before adding anything to it, inspect"
Write-Output "what it actually contains. A broken base checkout is not a reason to publish junk."
Write-Output ""

Write-Output "Current git status, exactly as reported by the repo:"
git status
Write-Output ""

$repoLocal = @(
    ".gitignore",
    "prepare-repo.ps1",
    "make-repo-artifacts.ps1",
    "README.md",
    "LICENSE",
    ".env.example",
    "tools/generate-icon.py",
    "tools/archil_store.py"
)
foreach ($f in $repoLocal) {
    if ((Test-Path "$PSScriptRoot/$f") -and -not (Test-Path "$root/$f")) {
        Copy-Item -Force "$PSScriptRoot/$f" "$root/$f"
    }
}

$iconPack = "$root/src/JoyNect.App/assets/icons.gen.xaml"
if (-not (Test-Path $iconPack)) {
    Write-Output "The Material 3 icon pack is missing from this copy:"
    Write-Output "  $iconPack"
    Write-Output ""
    Write-Output "That file is normally generated from the project's Material Symbols pack."
    Write-Output "This checkout does not include the generator, so the icon pack cannot be"
    Write-Output "rebuilt here. Restore the generator into this copy and run:"
    Write-Output "  cd tools/generate-icon && npm install && node generate-icon.mjs"
    Write-Output "or carry the icon pack from the canonical source repository when you do."
    Write-Output ""
}

Write-Output "Rebuilding the self-contained Win x64 executable and packaging it as a clean ZIP."
Remove-Item -Force "patterns.json" -ErrorAction SilentlyContinue
dotnet restore
dotnet publish src/JoyNect.App/JoyNect.App.csproj -c Release -r win-x64 --self-contained true -o publishes/win-x64 -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableSourceLink=false -p:DebugType=none -p:DebugSymbols=false

$zip = Join-Path $root "JoyNect-1.0.0-win-x64.zip"
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Force -Path "publishes/win-x64/*" -DestinationPath $zip

Write-Output ""
Write-Output "Build outputs produced:"
Write-Output "  publishes/win-x64/   self-contained executable and runtimes"
Write-Output "  JoyNect-1.0.0-win-x64.zip"
Write-Output ""

Write-Output "Current git status after the rebuild, exactly as reported by the repo:"
git status
Write-Output ""

Write-Output "Next steps before you publish to GitHub:"
Write-Output "  1. Confirm the repository at https://github.com/Scarneitor/JoyNect is the one you mean."
Write-Output "  2. Decide which of the rebuilt files should be committed and which should stay local."
Write-Output "  3. Add only what belongs in version control, then commit."
Write-Output "  4. Push only after the commit looks right on the remote."
