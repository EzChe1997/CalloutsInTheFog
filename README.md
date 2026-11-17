# Callouts in the Fog

**Survivor callouts for solo queue: what one survivor sees, all four know.**  
*Callouts para solo queue: lo que ve un sobreviviente, lo saben los cuatro.*

*[Español más abajo](#español)*

A free overlay for Dead by Daylight survivors: write down the killer's perks, add-ons and offering as you see them,
and they stay on screen with their icons. **Everyone in your group sees the same list**: friends in your Steam party
automatically, or anyone (solo queue teammates, Epic players) with a room code.

- **Type it**: Shift+T, a few letters ("noed", "pain res"), Enter. Search add-ons by what they do: "addon 15 slow".
- **Or say it** (optional): hold Shift+V and say "he has Pain Resonance and Deadlock". Speech recognition runs on your PC
  with [whisper.cpp](https://github.com/ggerganov/whisper.cpp), no internet.
- **Maps**: type "map coal" and a callout map shows up (for the whole room).
- **Light**: almost nothing runs in the background; the list and the map can show only while the ESC menu is open.
- English and Spanish (Settings → General → App language).

## Can I get banned?

The app **does not read the game's memory, does not inject anything into the game and does not modify its files**. It's a separate window drawn on top of the game,
like Discord's overlay, and everything on the list is written by a person. The source code is all in this repository.
This is a fan project, not affiliated with or endorsed by Behaviour Interactive.

## Install

1. Download `DbdOverlay.exe` from this repository and put it in its own folder (for example Documents\DbdOverlay).
2. Open it. Windows may say "Windows protected your PC" because the exe is not code-signed: "More info" → "Run anyway".
3. A red hook icon appears next to the clock. **Double-click it** for Settings.
4. The game must be in **Windowed Fullscreen / Borderless** for the overlay to show on top.

The app updates itself from here and only accepts versions signed by the project.

## Credits

- Callout maps (clock system): images by **Lethia**, from [hens333.com/callouts](https://hens333.com/callouts)
  (Zexov's version of the original by Broosley and Evo, from Hens' Discord). The app downloads them from that page when
  you pick a map; they are not included in this repository.
- Game data: [dbd.tricky.lol](https://dbd.tricky.lol). Icons: [DBD wiki](https://deadbydaylight.wiki.gg).
- Voice: [whisper.cpp](https://github.com/ggerganov/whisper.cpp).

## Build

`powershell -File build.ps1` (uses the C# compiler that ships with Windows, .NET Framework 4).

---

## Español

Overlay gratis para sobrevivientes de Dead by Daylight: anotás los perks, addons y la ofrenda del killer a medida que
los ves y quedan en pantalla con sus íconos. **Todo el grupo ve la misma lista**: los amigos de tu grupo de Steam
automáticamente, o cualquiera (randoms, jugadores de Epic) con un código de sala.

- **Escribiendo**: Shift+T, unas letras ("noed", "pain res"), Enter. Addons por lo que hacen: "addon 15 lento".
- **O hablando** (opcional): mantené Shift+V y decí "tiene Pain Resonance y Deadlock". El reconocimiento de voz corre en
  tu PC con [whisper.cpp](https://github.com/ggerganov/whisper.cpp), sin internet.
- **Mapas**: escribí "mapa coal" y aparece el plano con callouts (para toda la sala).
- **Liviana**: casi no hace nada de fondo; la lista y el mapa se pueden mostrar solo con el menú ESC abierto.

**¿Me pueden banear?** La app no lee la memoria del juego, no se mete en el juego y no modifica sus archivos. Es una ventana aparte dibujada
encima, como el overlay de Discord, y todo lo que muestra lo anota una persona. Es un proyecto de fans, sin relación con
Behaviour Interactive.

**Instalar**: bajá `DbdOverlay.exe` y seguí [LEEME.txt](LEEME.txt). Se actualiza sola desde acá; solo acepta versiones
firmadas.

**Créditos**: planos de **Lethia** ([hens333.com/callouts](https://hens333.com/callouts), versión de Zexov sobre la
original de Broosley y Evo, del Discord de Hens); la app los baja de esa página y no están en este repositorio.
Datos: [dbd.tricky.lol](https://dbd.tricky.lol). Íconos: [wiki de DBD](https://deadbydaylight.wiki.gg).
Voz: [whisper.cpp](https://github.com/ggerganov/whisper.cpp).
