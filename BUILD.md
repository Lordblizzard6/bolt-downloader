# Guía de Instalación y Compilación

## Prerequisitos

### Instalar .NET 8 SDK

1. **Descargar .NET 8 SDK** desde:
   - Sitio oficial: https://dotnet.microsoft.com/download/dotnet/8.0
   - Descarga directa: https://dotnet.microsoft.com/download/dotnet/thank-you/sdk-8.0.100-windows-x64-installer

2. **Ejecutar el instalador** y seguir los pasos

3. **Verificar instalación**:
   ```powershell
   dotnet --version
   ```
   Debería mostrar: `8.0.xxx`

## Compilar el Proyecto

### Opción 1: Compilación Normal

```powershell
cd "c:\QwenTest\CascadeProjects\windsurf-project"
dotnet restore
dotnet build --configuration Release
```

### Opción 2: Ejecutar sin compilar

```powershell
dotnet run
```

### Opción 3: Crear ejecutable portable

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

El ejecutable estará en: `bin\Release\net8.0-windows\win-x64\publish\BoltDownloader.exe`

## Ejecutar la Aplicación

Después de compilar:

```powershell
cd bin\Release\net8.0-windows\win-x64\publish
.\BoltDownloader.exe
```

O directamente con:

```powershell
dotnet run --configuration Release
```

## Solución de Problemas

### Error: 'dotnet' no se reconoce
- Asegúrese de haber instalado .NET 8 SDK
- Reinicie PowerShell/Terminal después de la instalación
- Verifique que `C:\Program Files\dotnet` esté en el PATH del sistema

### Error de compilación
- Ejecute `dotnet restore` primero
- Verifique que tenga conexión a Internet para descargar paquetes
- Cierre Visual Studio u otros IDEs que puedan tener el proyecto abierto

### Error al ejecutar
- Asegúrese de estar en Windows 10/11
- Verifique que .NET 8 Runtime esté instalado

## Construir la extensión de navegador (Windows)

La carpeta de la extensión está en `Extensions/Chrome/`.

1. Abrir PowerShell en la raíz del proyecto y permitir la ejecución del script (solo para esta sesión):

   ```powershell
   Set-ExecutionPolicy Bypass -Scope Process -Force
   .\Extensions\Chrome\build.ps1
   ```

   Esto generará un ZIP `bolt-helper_<version>.zip`. Si se detecta Google Chrome instalado, el script también intentará crear un `.crx` usando la función interna de empaquetado de Chrome. Si existe `Extensions/Chrome.pem` (clave de firma), la usará para mantener el mismo ID.

2. Opciones del script:

   ```powershell
   # Especificar nombre del ZIP de salida
   .\Extensions\Chrome\build.ps1 -OutputZip "bolt-helper.zip"

   # Solo ZIP, omitir .crx
   .\Extensions\Chrome\build.ps1 -NoCrx
   ```

3. Cargar la extensión en Chrome/Edge:

   - Abrir `chrome://extensions/`
   - Activar “Developer mode” (Modo desarrollador)
   - Usar “Load unpacked” para cargar la carpeta `Extensions/Chrome/`,
     o “Pack extension” para empaquetar con Chrome, o arrastrar el `.crx`/`.zip` (según navegador).

Notas:

- El script busca Chrome en rutas comunes (`Program Files`/`LocalAppData`). Si no lo encuentra, omite el `.crx` y deja el `.zip` listo.
- Para mantener el ID entre compilaciones `.crx`, coloca `Chrome.pem` en `Extensions/`.

## Construir la extensión de navegador (Linux/Mac)

La carpeta de la extensión está en `Extensions/Chrome/`.

1. Dar permisos de ejecución al script de empaquetado:

   ```bash
   cd Extensions/Chrome
   chmod +x build.sh
   ```

2. Generar el ZIP de la extensión (se detecta la versión desde `manifest.json`):

   ```bash
   ./build.sh           # crea bolt-helper_*.zip
   # o bien especificar nombre de salida
   ./build.sh bolt-helper.zip
   ```

3. Cargar la extensión en Chrome/Chromium:

   - Abrir `chrome://extensions/`
   - Activar “Developer mode” (Modo desarrollador)
   - Usar “Load unpacked” si desea cargar desde carpeta (seleccione `Extensions/Chrome`),
     o “Pack extension” para empaquetar con Chrome, o “Load” un `.zip` en navegadores que lo soporten.

Notas:

- La extensión ahora soporta i18n (en, es, de, fr) mediante `_locales/`. Chrome selecciona el idioma según la configuración del navegador/sistema.
- Para modificar textos, edite los archivos `Extensions/Chrome/_locales/<lang>/messages.json`.
