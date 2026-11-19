# Сборка подписанного APK. Android-инструменты Unity не работают с путями из не-ASCII символов (русские буквы в пути проекта),
# поэтому проект зеркалируется в ASCII-папку (Library там сохраняется между сборками) и собирается оттуда.
# Запуск: powershell -File tools/build-apk.ps1
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $PSScriptRoot
$dst = 'C:\BollBuild\proj'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.5.2f1\Editor\Unity.exe'
New-Item -ItemType Directory -Force $dst | Out-Null
robocopy $src $dst /MIR /XD Library Logs Temp Builds obj .git UserSettings /XF *.apk /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy: $LASTEXITCODE" }
# ключ копируется в ASCII-папку на время сборки (вне репозитория)
$keys = 'C:\BollBuild\keys'
New-Item -ItemType Directory -Force $keys | Out-Null
Copy-Item 'D:\создание игр\Boll Keys\*' $keys -Force
$env:BOLL_KEYS_DIR = $keys
$env:DOTNET_Thread_UseSpecialUserModeApc = "1"   # обход сбоя UnityLinker при включённых теневых стеках Windows
$p = Start-Process $unity -Wait -PassThru -ArgumentList '-batchmode','-nographics','-quit','-projectPath',$dst,'-buildTarget','Android','-executeMethod','Boll.EditorTools.BollBuild.Build','-logFile','C:\BollBuild\build.log'
$code = $p.ExitCode
New-Item -ItemType Directory -Force (Join-Path $src 'Builds') | Out-Null
if (Test-Path "$dst\Builds\BollOffline.apk") { Copy-Item "$dst\Builds\BollOffline.apk" (Join-Path $src 'Builds\BollOffline.apk') -Force; Write-Host "APK: $src\Builds\BollOffline.apk" }
Write-Host "Unity exit code: $code (журнал: C:\BollBuild\build.log)"
exit $code
