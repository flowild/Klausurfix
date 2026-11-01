Option Explicit
Dim shell, files, root, starter, shortcutPath, shortcut
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
root = files.GetParentFolderName(WScript.ScriptFullName)
starter = files.BuildPath(root, "Klausurfix.vbs")
shortcutPath = files.BuildPath(shell.SpecialFolders("Desktop"), "Klausurfix.lnk")
If Not files.FileExists(starter) Then
    MsgBox "Der Klausurfix-Starter wurde nicht gefunden.", vbCritical, "Klausurfix"
    WScript.Quit 1
End If
If files.FileExists(shortcutPath) Then
    MsgBox "Auf dem Desktop gibt es bereits eine Verknuepfung namens Klausurfix. Sie wurde nicht ueberschrieben.", vbExclamation, "Klausurfix"
    WScript.Quit 1
End If
Set shortcut = shell.CreateShortcut(shortcutPath)
shortcut.TargetPath = files.BuildPath(shell.ExpandEnvironmentStrings("%SystemRoot%"), "System32\wscript.exe")
shortcut.Arguments = """" & starter & """"
shortcut.WorkingDirectory = root
shortcut.Description = "Klausurfix lokal starten"
shortcut.IconLocation = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\shell32.dll,13"
shortcut.Save
MsgBox "Die Verknuepfung Klausurfix wurde auf dem Desktop erstellt.", vbInformation, "Klausurfix"
