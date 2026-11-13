# Writes TimeLightMap.mod: an empty Kenshi FCS data file (format 16, no records).
# The game only discovers a mod folder <mods>/<Name>/ when <Name>.mod exists in it, and loads that file as game data;
# a code mod with no data of its own still needs one. Layout:
#   int32 16, int32 version, string author, string description, string dependencies, string references, int32 0, int32 0
#   (a string is an int32 byte count followed by the bytes, no terminator)
# Run: powershell -File make-mod-file.ps1 -Out TimeLightMap.mod
param([string]$Out = "TimeLightMap.mod")
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
function Put([string]$s) { $b = [System.Text.Encoding]::UTF8.GetBytes($s); $bw.Write([int]$b.Length); $bw.Write($b) }
$bw.Write([int]16)
$bw.Write([int]1)
Put "Beep Is Too Strong team"
Put "TimeLight Map 1.0.0: a live 3D map in a corner of the game window. Code mod for MeitouClient 0.17.4, no game data. Console command: topcam. GPL-3.0-or-later."
Put ""
Put ""
$bw.Write([int]0)
$bw.Write([int]0)
$bw.Flush()
[System.IO.File]::WriteAllBytes($Out, $ms.ToArray())
Write-Output ("wrote " + $Out + " (" + $ms.Length + " bytes)")
