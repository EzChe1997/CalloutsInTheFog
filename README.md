# DBD Killer Overlay

Overlay para Dead by Daylight (sobreviviente): anotás por voz los perks, addons y la ofrenda del killer
y quedan en pantalla con sus íconos. La lista se comparte sola con los amigos del mismo grupo de Steam.

- No lee la memoria del juego: todo lo que aparece lo anota alguien por voz.
- Voz con [whisper.cpp](https://github.com/ggerganov/whisper.cpp), local y sin internet.
- Datos de perks/addons/ofrendas: [dbd.tricky.lol](https://dbd.tricky.lol) e íconos de la [wiki de DBD](https://deadbydaylight.wiki.gg).

## Créditos

- Planos de mapas con el sistema de reloj: imágenes de **Lethia**, de la página de callouts de
  [hens333.com](https://hens333.com/callouts) (versión de Zexov sobre la original de Broosley y Evo, del Discord de Hens).
  La app las baja de esa página cuando elegís un mapa; no se incluyen en este repositorio.
- Datos del juego: [dbd.tricky.lol](https://dbd.tricky.lol). Íconos: [wiki de DBD](https://deadbydaylight.wiki.gg).
- Voz: [whisper.cpp](https://github.com/ggerganov/whisper.cpp).

## Instalar

Bajá `DbdOverlay.exe` de este repositorio (o el zip que te pasen) y seguí [LEEME.txt](LEEME.txt).
La app se actualiza sola desde acá; solo acepta versiones firmadas.

## Compilar

`powershell -File build.ps1` (usa el compilador de C# que trae Windows, .NET Framework 4).
