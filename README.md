# DBD Killer Overlay

Overlay para Dead by Daylight (sobreviviente): anotás por voz los perks, addons y la ofrenda del killer
y quedan en pantalla con sus íconos. La lista se comparte sola con los amigos del mismo grupo de Steam.

- No lee la memoria del juego: todo lo que aparece lo anota alguien por voz.
- Voz con [whisper.cpp](https://github.com/ggerganov/whisper.cpp), local y sin internet.
- Datos de perks/addons/ofrendas: [dbd.tricky.lol](https://dbd.tricky.lol) e íconos de la wiki.

## Instalar

Bajá `DbdOverlay.exe` de este repositorio (o el zip que te pasen) y seguí [LEEME.txt](LEEME.txt).
La app se actualiza sola desde acá; solo acepta versiones firmadas.

## Compilar

`powershell -File build.ps1` (usa el compilador de C# que trae Windows, .NET Framework 4).
