# Cómo colaborar / How to contribute

## Español

Cuida tu Batería es un programa gratuito y de código abierto (GPL-3.0) para laptops viejas con Windows. Lo mantiene una sola persona, Alexis Reyes, en ratos libres. No hay versión paga ni soporte pago, y no la habrá.

Si encontraste un problema, abre un issue con: tu versión de Windows, la versión de Cuida tu Batería, el modelo de la laptop y qué esperabas que pasara. Si la cifra de salud o de autonomía te parece rara, incluye lo que muestra el programa y lo que marca Windows. Las cifras de salud y autonomía son estimaciones, y la meta es que sean honestas, no bonitas.

Si quieres proponer un cambio de código, abre primero un issue para conversarlo. Después, un pull request pequeño, con una sola idea. Se acepta bajo la misma licencia, GPL-3.0.

Para compilar hace falta el SDK de .NET 8 o más nuevo. En Windows (Git Bash) o en Linux:

```
./build.sh
```

Deja la app en `build/app` y el instalador en `build/installer`, e imprime el SHA-256 del instalador. `./build.sh preview` compila una versión de prueba con datos simulados; esa nunca se publica. Las versiones públicas se compilan en GitHub Actions, y su SHA-256 es el que vale.

Para fallas de seguridad no uses issues públicos: sigue SECURITY.md.

Respondo cuando puedo, sin plazo prometido.

## English

Cuida tu Batería is a free and open source program (GPL-3.0) that looks after the battery of older Windows laptops. One person maintains it, Alexis Reyes, in spare time. There is no paid version and no paid support, and there will not be.

If you found a problem, open an issue with your Windows version, the Cuida tu Batería version, the laptop model, and what you expected to happen. If a health or runtime figure looks wrong, include what the program shows and what Windows reports. Health and runtime figures are estimates, and the goal is for them to be honest, not pretty.

If you want to propose a code change, open an issue first to talk it over. Then send a small pull request with a single idea. Contributions are accepted under the same license, GPL-3.0.

To build you need the .NET SDK, version 8 or newer. On Windows (Git Bash) or Linux:

```
./build.sh
```

It leaves the app in `build/app` and the installer in `build/installer`, and prints the SHA-256 of the installer. `./build.sh preview` builds a test version with simulated data; that one is never published. Public releases are built in GitHub Actions, and their SHA-256 is the one that counts.

For security problems, do not use public issues: follow SECURITY.md.

I answer when I can, with no promised deadline.
