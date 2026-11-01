Option Explicit
Dim shell, files, root, scriptPath, command
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
root = files.GetParentFolderName(WScript.ScriptFullName)
scriptPath = files.BuildPath(root, "scripts\start-klausurfix.ps1")
If Not files.FileExists(scriptPath) Then
    MsgBox "Der Klausurfix-Starter wurde nicht gefunden. Bitte den gesamten Programmordner beibehalten.", vbCritical, "Klausurfix"
    WScript.Quit 1
End If
command = "powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File """ & scriptPath & """"
shell.Run command, 0, False
