# Cuida tu Batería

Una app gratuita para Windows que te avisa cuándo conectar y cuándo desconectar tu laptop, y te dice cuánto te dura la batería con tu uso de verdad, sobre todo cuando se va la luz.

Una vez reviví una canaimita que estaba súper muerta. Esta app te avisa a tiempo para que tu laptop no llegue a ese punto.

*Alexis Reyes, Reyes Project*

[English](README.en.md)

## Qué hace

- Te avisa cuando la batería llega a tu número de arriba (80 % de entrada) para que desconectes, y cuando baja a tu número de abajo (20 %) para que conectes. El aviso sale al lado del reloj, dondequiera que tengas la barra de tareas, y no te quita el foco. Si estás en una presentación, un juego o un video a pantalla completa, espera.
- «Luego» te lo repite en 15 minutos. La X lo calla hasta el próximo ciclo.
- Te dice cuánto te queda. Con batería, "Te queda" usa tu propio ritmo de gasto de los últimos días. Conectada, responde la pregunta de siempre en Venezuela: si se va la luz ahora, cuánto tienes. Cuenta hasta el nivel en que Windows apaga la laptop, no hasta 0 %.
- Con batería, te dice cuántos vatios está gastando la laptop en ese momento y si es un uso ligero, normal o pesado. Así entiendes por qué un día la pila dura tres horas y otro día hora y media.
- Mientras carga, te dice en cuánto llega a tu número de arriba, con el ritmo real de tu cargador.
- Modo apagón: los días de cortes, un botón la deja cargar a 100 % sin pedirte que la desconectes durante 24 horas.
- Vive conectada: para la laptop que hace de punto de venta o vive en un escritorio. No te pide desconectarla nunca y sigue avisando cuando baja.
- Si tu laptop ya detiene la carga sola (Lenovo, ASUS, Dell, Windows 11), lo nota y no te molesta.
- Mide la salud de la batería contra lo que traía nueva, te dice cuándo se fabricó (si la batería lo guarda), la muestra mes a mes y la exporta como imagen con un veredicto para el técnico.
- Cuenta los ciclos de carga cuando tu laptop no los cuenta.
- Explica el informe de batería de Windows en palabras simples.
- Te dice dónde se activa el límite de carga de fábrica en Lenovo, ASUS, Dell, Samsung, Acer y HP de negocios. Ese límite sí corta la carga.
- Los domingos en la tarde, un resumen corto de tu semana. Sin regaños.
- Se maneja con el teclado (Tab, Enter, Esc), está en español y en inglés, y se ve nítida en cualquier pantalla.

## Qué no hace

- No corta la carga. Windows no permite que un programa apague el cargador. La app te avisa y tú desconectas.
- No se conecta a internet. Nada sale de tu laptop.
- No pide permisos de administrador. Se instala solo para tu usuario.
- No funciona en Canaima GNU/Linux: es solo para Windows 10 y 11 (32 o 64 bits).
- En una computadora de escritorio con UPS se queda tranquila: la batería que ve Windows es la de la UPS.

## Instalar

Descarga `CuidaTuBateriaSetup-6.1.0.exe` de la [página de versiones](https://github.com/alexisreyes02/cuida-tu-bateria/releases) y ábrelo. No hace falta instalar nada más (usa .NET Framework 4.6.1, que ya viene con Windows 10 y 11).

Mientras el instalador no tenga firma digital, Windows puede mostrar "Windows protegió tu PC". Para seguir: "Más información" y luego "Ejecutar de todas formas". Antes de abrirlo, puedes comprobar que el archivo es el original. En PowerShell:

```
Get-FileHash ".\CuidaTuBateriaSetup-6.1.0.exe" -Algorithm SHA256
```

El resultado tiene que ser igual al SHA-256 publicado junto a la descarga.

Para instalar sin ventanas (por ejemplo, desde winget): `CuidaTuBateriaSetup-6.1.0.exe /silent`. Una instalación nueva así queda con "Iniciar con Windows" encendido; una actualización respeta lo que tenías. Para quitarla sin ventanas: `uninstall.exe /uninstall /silent`, desde la carpeta de instalación (`%LOCALAPPDATA%\Programs\CuidaTuBateria`).

## Dónde guarda tus cosas

Todo queda en `%LOCALAPPDATA%\CuidaTuBateria`, en esta laptop:

- `settings.ini`: tus números y tus preferencias.
- `uso.csv`: una línea cada vez que cambia el porcentaje o el cargador, y una cada 10 minutos. Se guardan 30 días y se escriben al disco cada media hora, al suspender y al cerrar.
- `salud.txt`: la última medida de salud y los meses. Se mide una vez al día como máximo, unos minutos después de encender.
- `errores.log`: solo si algo falla.

Al quitar la app, el desinstalador te pregunta si quieres conservar estos archivos. La imagen de salud que guardes en Imágenes muestra la marca y el modelo de tu laptop; la compartes tú si quieres.

## Cómo calcula el tiempo que te queda

Toma los tramos en que la laptop estuvo con batería en los últimos 14 días, suma cuánto bajó el porcentaje y lo divide entre las horas. Si la laptop lleva más de 5 minutos desconectada, mezcla ese ritmo del momento (60 %) con el de los días anteriores (40 %). Es una regla práctica, no una cifra de laboratorio. No cuentan los cortes de más de 16 minutos (suspensión, app cerrada) ni las lecturas de 98 % o más con batería: muchas laptops, la HP entre ellas, se quedan marcando 100 % unos minutos después de desconectarlas y luego se ponen al día de golpe. El tiempo llega hasta el nivel crítico de Windows (de entrada, 5 %), que es donde la laptop se apaga. Mientras no tiene tus datos, muestra la cuenta de Windows con la palabra "Unas" delante y dice que todavía no es la suya: Windows la saca de otras semanas, y con un uso más pesado puede quedarse corta.

## Por qué 80 % y 20 %

A una batería de ion de litio la desgastan más las horas llena al tope, sobre todo con calor, y las descargas muy hondas. Battery University no fija "80 y 20": recomienda cargas parciales y evitar la carga completa con calor ([BU-808](https://batteryuniversity.com/article/bu-808-how-to-prolong-lithium-based-batteries)). 80 y 20 es una regla práctica que sigue ese consejo. Los números se cambian en la app cuando quieras.

La salud se califica así: 90 % o más, excelente; de 80 a 89, buena; de 60 a 79, gastada; menos de 60, débil. Apple, por ejemplo, considera 80 % el nivel esperado al final de la vida útil de una batería ([Apple](https://support.apple.com/en-us/ht201585)).

## Fuentes de la guía por marca

- Lenovo: [Lenovo Vantage, umbral de carga y modo de conservación](https://support.lenovo.com/hn/en/solutions/ht103159)
- ASUS: [ASUS Battery Health Charging](https://www.asus.com/ar/support/FAQ/1032726/)
- Dell: [Cómo mejorar el rendimiento de la batería de una laptop Dell](https://www.dell.com/support/kbdoc/en-bw/000123091)
- Samsung: [Galaxy Book battery](https://www.samsung.com/us/support/galaxy-battery/galaxy-book-battery/)
- Acer: [Límite de carga de la batería en portátiles Acer](https://community.acer.com/es/kb/articles/19027-funcion-de-limite-de-carga-de-la-bateria-en-portatiles-acer)
- HP de negocios: [HP Battery Health Manager](https://support.hp.com/document/c06179452)

La capacidad nueva de la HP 14-cf, 14-ce, 15-cs y 15-da (unos 41 Wh, batería HT03XL) sale de la ficha de esa batería y se muestra como "según la ficha": la HT03XL también existe en versión de 45 Wh, así que es un estimado.

## Compilar

Necesitas el SDK de .NET 8 o más nuevo.

```
./build.sh
```

Deja la app en `build/app` y el instalador en `build/installer`, e imprime el SHA-256 del instalador. `./build.sh preview` compila una versión de prueba con datos simulados; esa nunca se publica, y la compilación normal se niega a terminar si encuentra rastros de ella. Las versiones públicas se compilan en GitHub Actions, y su SHA-256 es el que vale.

## Privacidad, garantía y marcas

Cuida tu Batería no se conecta a internet ni envía datos a nadie: tus ajustes, tu registro de uso y la salud de la batería se guardan solo en tu laptop, y puedes borrarlos cuando quieras.

Se ofrece gratis y tal cual, sin garantía de ningún tipo, según la licencia GPL-3.0 o posterior. Las cifras de salud y autonomía son estimaciones.

Reyes Project no está afiliado a Lenovo, ASUS, Dell, Samsung, Acer, HP ni Microsoft. Lenovo Vantage, MyASUS, Dell Power Manager, Samsung Settings, Acer Care Center, HP Battery Health Manager, Galaxy Book e IBM Plex son marcas de sus respectivos dueños; se nombran solo para indicar dónde está cada ajuste.

## Licencia

Cuida tu Batería es software libre bajo la [GNU General Public License, versión 3 o posterior](LICENSE). Copyright (C) 2026 Alexis Reyes (Reyes Project).

Las tipografías van dentro de la app bajo la SIL Open Font License 1.1: Literata (The Literata Project Authors) e IBM Plex Sans (IBM Corp., nombre reservado "Plex"). Sus licencias están en `assets/fonts`.
