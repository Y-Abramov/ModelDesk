$base    = [System.IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path)
. (Join-Path $base "..\Shared\BuildTpm.Common.ps1")

Build-AbrTpm -Base $base -TpmName "ModelDesk" `
    -DllPath "bin\Debug\net48\Abr.ModelDesk.dll" `
    -PluginFiles @("ModelDesk.plugin", "t_modeldesk_tab.plugin") `
    -NeedsSetupExe
