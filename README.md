# PDF Editor

Aplicación de escritorio en **C# / WPF / .NET 10** con tema oscuro e interfaz en español. Permite editar texto e imágenes de documentos PDF, dibujar o importar firmas, colocarlas sobre las páginas y guardar una copia con firmas visuales o con firma digital mediante certificado.

## Abrir la aplicación

La publicación para Windows de 64 bits está en:

```text
artifacts\PdfEditor-win-x64\PdfEditor.exe
```

Haz doble clic en `PdfEditor.exe`. Esta publicación incluye .NET; conserva la carpeta completa con sus archivos al copiarla a otro equipo. Está destinada a Windows 10 de 64 bits (22H2) y Windows 11.

También puedes abrir un documento desde la línea de comandos:

```powershell
& ".\artifacts\PdfEditor-win-x64\PdfEditor.exe" "C:\Documentos\contrato.pdf"
```

### Ejecutar con el código actual

Haz doble clic en **`run.bat`**, situado en la raíz del proyecto. El lanzador cierra las instancias anteriores de PDF Editor, compila el código actual en `artifacts\run\` y abre la aplicación actualizada. Requiere el SDK de .NET 10.

También puedes pasarle el PDF que quieres abrir:

```powershell
.\run.bat "C:\Documentos\contrato.pdf"
```

## Uso

1. Pulsa **Abrir PDF** o arrastra un PDF a la ventana.
2. En **Mis firmas**, pulsa **Crear** para dibujar con ratón/lápiz, o **Importar** para añadir una imagen PNG/JPG.
3. Pon un nombre y guarda la firma en tu biblioteca. La importación permite quitar el fondo blanco y recortar los márgenes.
4. Arrastra la firma a la página, haz doble clic en su tarjeta o pulsa **Colocar firma seleccionada**.
5. Arrastra la firma para moverla. Usa el tirador de la esquina inferior derecha para cambiar su tamaño manteniendo la proporción. Puedes colocar varias firmas en distintas páginas.
6. Pulsa **Guardar copia** para incorporar las firmas visuales al PDF, o **Firmar digitalmente** para utilizar un certificado.
7. Tras guardar, **Ver PDF guardado** abre el resultado con el lector predeterminado de Windows.

Las posiciones se mantienen al cambiar el zoom. La exportación tiene en cuenta la rotación, el recorte y el origen de las páginas. El contenido del PDF se conserva como PDF, con sus imágenes y texto, y las firmas se añaden sobre él.

### Firma digital

- **Archivo PFX/P12:** selecciona el archivo, introduce su contraseña y pulsa **Revisar certificado**. Después pulsa **Firmar y guardar**.
- **Certificados de Windows:** elige uno de los almacenes personales del usuario actual o del equipo. Se muestran los certificados vigentes que incluyen clave privada y permiten firmar.
- Puedes indicar **motivo** y **lugar**.
- Si tienes una firma seleccionada en la página, puedes usarla como apariencia del campo digital. Ese campo se puede consultar en un lector PDF compatible con firmas.
- La firma criptográfica es **CMS/PKCS#7 separada, con SHA-256**, aplicada al documento final. Cada exportación incorpora una firma digital.
- La contraseña del PFX se usa durante la operación. La aplicación guarda las imágenes de las firmas, pero no almacena el PFX ni su contraseña.

Los PDF que ya contienen una firma digital se abren en **modo lectura**. Los PDF protegidos solicitan contraseña; la exportación requiere la contraseña del propietario y genera una copia desencriptada. El zoom del visor va del 35 % al 300 %.

### Biblioteca local

Las firmas se guardan automáticamente en:

```text
%LocalAppData%\PdfEditor\Signatures\
```

Cada firma tiene un PNG y un JSON con su nombre, fecha y origen. Puedes renombrarla o eliminarla desde el panel. Al eliminarla de la biblioteca, las instancias que ya has colocado en el documento actual siguen disponibles durante esa sesión.

### Atajos

| Atajo | Acción |
|---|---|
| `Ctrl+O` | Abrir PDF |
| `Ctrl+S` | Guardar copia con firmas visuales |
| `Ctrl+Mayús+S` | Firmar digitalmente |
| `Ctrl+Z` / `Ctrl+Y` | Deshacer / rehacer la edición de firmas |
| `Ctrl+D` | Duplicar la firma seleccionada |
| `Supr` | Quitar la firma seleccionada del PDF |
| `Ctrl+RePág` / `Ctrl+AvPág` | Página anterior / siguiente |
| `Ctrl+rueda del ratón` | Cambiar el zoom |

## Desarrollo

Requiere Windows y el **SDK de .NET 10**. La primera restauración descarga PDFsharp desde NuGet.

Desde la raíz del proyecto:

```powershell
dotnet build "PdfEditor.slnx" -c Release
dotnet run --project "PdfEditor\PdfEditor.csproj"
```

Para publicar la aplicación con .NET incluido:

```powershell
dotnet publish "PdfEditor\PdfEditor.csproj" -c Release -p:PublishProfile=Windows
```

### Comprobaciones de integración

```powershell
dotnet run --project "tests\PdfEditor.Checks\PdfEditor.Checks.csproj" -c Release
```

El ejecutable de comprobaciones genera documentos de prueba y capturas en `artifacts\checks\`. Verifica:

- Persistencia, renombrado y eliminación de firmas.
- Dibujo recortado, PNG transparente e importación con eliminación del blanco.
- Posición, orientación y transparencia de firmas visuales y campos digitales en ocho variantes de páginas: 0°, 90°, 180°, 270°, con y sin recortes/orígenes desplazados.
- Conservación del contenido original y exportación de PDF protegidos.
- Firma CMS con PFX y con un certificado recuperado del almacén real de Windows; verifica SHA-256 y el rechazo de un documento alterado.
- Arrastre, redimensionado, deshacer/rehacer, editor de firmas, selector de certificados, exportación desde la interfaz y enlaces de datos WPF.

Las pruebas crean su propio certificado temporal, lo retiran del almacén personal al terminar y eliminan su clave y archivo PFX. Las ventanas de prueba se abren y cierran automáticamente.

### Estructura

```text
PdfEditor/
  Controls/           Controles para mover y redimensionar firmas
  Infrastructure/     Notificaciones, comandos y convertidores WPF
  Models/             Firmas, colocaciones y solicitudes de firma digital
  Services/           Visor, geometría PDF, exportación, biblioteca y certificados
  Themes/             Recursos y estilos oscuros
  ViewModels/         Estado y acciones del editor
  Views/              Diálogos de dibujo, importación y certificados
  MainWindow.xaml     Ventana principal
tests/
  PdfEditor.Checks/    Comprobaciones de integración sin servicios externos
```

## Dependencias

- [PDFsharp-WPF 6.2.4](https://docs.pdfsharp.net/) — modificación de PDF y firma digital; licencia MIT.
- `Windows.Data.Pdf` — renderizado de documentos con el motor de Windows.
- APIs criptográficas y controles WPF de .NET 10.

Los avisos de las dependencias están en `THIRD-PARTY-NOTICES.md`.
