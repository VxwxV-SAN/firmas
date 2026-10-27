using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Formats.Asn1;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfEditor;
using PdfEditor.Controls;
using PdfEditor.Models;
using PdfEditor.Services;
using PdfEditor.Views;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfDocument = PdfSharp.Pdf.PdfDocument;

namespace PdfEditor.Checks;

internal static class Program
{
    private static int _checks;
    private static readonly string Output = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "checks"));
    private static readonly byte[] PaperColor = [219, 231, 245];

    [STAThread]
    private static int Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("es-ES");
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PdfEditor;component/Themes/Dark.xaml")
        });
        var exitCode = 0;
        application.Startup += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                await RunChecks();
                Console.WriteLine($"OK: {_checks} comprobaciones. Resultados: {Output}");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { application.Shutdown(exitCode); }
        };
        application.Run();
        return exitCode;
    }

    private static async Task RunChecks()
    {
        var image = TestImage();
        var store = new SignatureStore(Path.Combine(Output, "library-" + Guid.NewGuid().ToString("N")));
        var asset = store.Add("Firma de prueba", SignatureKind.Imported, image);
        Assert(store.Load().Single().Name == asset.Name, "Persistencia de la biblioteca");
        store.Rename(asset, "Firma renombrada");
        Assert(store.Load().Single().Name == "Firma renombrada", "Renombrado persistente");
        TestDrawingAndImport();
        TestEditObjectSnapshot();
        TestContentIndexAndTextExport();
        TestLegacyMigration();

        var original = Path.Combine(Output, "original.pdf");
        CreateFixture(original);
        var originalHash = SHA256.HashData(File.ReadAllBytes(original));
        using var source = await PdfSession.OpenAsync(original);
        Assert(source.CanExport && source.PageCount == 8, "Apertura de PDF con ocho variantes de página");
        var placements = source.Pages.Select((page, index) => new PlacementState(Guid.NewGuid(), asset,
            index, page.WidthDip, page.HeightDip, new Rect(.18, .35, .32, .14))).ToArray();
        var exporter = new PdfExporter();
        var visual = Path.Combine(Output, "firmas_visuales.pdf");
        await exporter.ExportAsync(source, placements, visual);
        using (var result = await PdfSession.OpenAsync(visual))
        {
            Assert(result.PageCount == source.PageCount && result.CanExport, "PDF visual conserva páginas y es editable");
            for (var index = 0; index < result.PageCount; index++)
            {
                var bitmap = await result.RenderAsync(index);
                CheckRenderedSignature(bitmap, placements[index].Bounds, $"Superposición en página {index + 1}");
            }
        }
        using (var pdf = PdfReader.Open(visual, PdfDocumentOpenMode.Import))
        {
            Assert(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>().All(p => p.Resources.Elements.GetDictionary("/Font")?.Elements.Count > 0),
                "Las fuentes y el texto original siguen en el PDF");
            Assert(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>().All(p => Encoding.Latin1.GetString(p.Contents.CreateSingleContent().Stream.UnfilteredValue).Contains("Tj")),
                "El contenido original conserva sus operadores de texto");
        }
        await ExpectThrows<InvalidOperationException>(() => exporter.ExportAsync(source, placements, original),
            "La exportación requiere un destino diferente al original");

        var pfxPath = Path.Combine(Output, "temporary-" + Guid.NewGuid().ToString("N") + ".pfx");
        var password = Guid.NewGuid().ToString("N");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=PdfEditor prueba " + Guid.NewGuid().ToString("N"),
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(2));
        File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, password));
        try
        {
            using var loaded = CertificateService.LoadPfx(pfxPath, password);
            Assert(loaded.HasPrivateKey, "Carga de PFX con clave privada");
            await ExpectThrows<CryptographicException>(() => Task.Run(() => CertificateService.LoadPfx(pfxPath, "incorrecta").Dispose()),
                "Rechazo de una contraseña PFX incorrecta");
            using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
            await ExpectThrows<InvalidOperationException>(() => Task.Run(() => CertificateService.EnsureUsable(publicOnly)),
                "Rechazo de certificados sin clave privada");

            for (var index = 0; index < source.PageCount; index++)
            {
                var signedPath = Path.Combine(Output, $"firmado_pfx_pagina_{index + 1}.pdf");
                using var signing = new SigningRequest(CertificateService.LoadPfx(pfxPath, password), "Prueba de firma", "Windows", true);
                await exporter.ExportAsync(source, placements, signedPath, signing, placements[index].Id);
                CheckCms(signedPath, certificate.Thumbprint, testTamper: index == 0);
                using var result = await PdfSession.OpenAsync(signedPath);
                Assert(!result.CanExport, "Detección de firma digital existente");
                var bitmap = await result.RenderAsync(index);
                CheckRenderedSignature(bitmap, placements[index].Bounds, $"Apariencia digital en página {index + 1}");
            }
            var invisiblePath = Path.Combine(Output, "firmado_sin_apariencia.pdf");
            using (var signing = new SigningRequest(CertificateService.LoadPfx(pfxPath, password), "Prueba", "", false))
                await exporter.ExportAsync(source, [], invisiblePath, signing);
            CheckCms(invisiblePath, certificate.Thumbprint);

            await CheckWindowsCertificate(pfxPath, password, exporter, source);
            await CheckProtectedPdf(exporter, asset);
            await CheckInterface(original, image, store, pfxPath, password);
        }
        finally { File.Delete(pfxPath); }
        Assert(SHA256.HashData(File.ReadAllBytes(original)).SequenceEqual(originalHash), "El PDF de entrada permanece íntegro");
        store.Delete(asset);
        Assert(store.Load().Count == 0, "Eliminación persistente de una firma");
        Directory.Delete(store.FolderPath, true);
    }

    private static void CreateFixture(string path)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = "Documento de prueba · PDF Editor";
        for (var index = 0; index < 8; index++)
        {
            var page = pdf.AddPage();
            page.MediaBox = index < 4 ? new PdfRectangle(new XPoint(0, 0), new XPoint(600, 800)) :
                new PdfRectangle(new XPoint(20, 30), new XPoint(620, 830));
            var crop = index < 4 ? page.MediaBox : new PdfRectangle(new XPoint(60, 80), new XPoint(560, 760));
            if (index >= 4) page.CropBox = crop;
            // Independent raw PDF coordinates provide the reference background.
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
                $"q {PaperColor[0] / 255d:R} {PaperColor[1] / 255d:R} {PaperColor[2] / 255d:R} rg {crop.X1:R} {crop.Y1:R} {crop.Width:R} {crop.Height:R} re f Q\n")));
            using (var graphics = XGraphics.FromPdfPage(page))
            {
                graphics.DrawString($"Contenido original - pagina {index + 1}", new XFont("Segoe UI", 14),
                    XBrushes.DarkSlateGray, new XPoint(95, 145));
            }
            page.Rotate = index % 4 * 90;
        }
        pdf.Save(path);
    }

    private static BitmapSource TestImage()
    {
        const int width = 160, height = 80;
        var bytes = new byte[width * height * 4];
        for (var y = 4; y < height - 4; y++)
        for (var x = 4; x < width - 4; x++)
        {
            if (x > width * .4 && x < width * .6 && y > height * .4 && y < height * .6) continue;
            var color = (x < width / 2, y < height / 2) switch
            {
                (true, true) => new byte[] { 217, 44, 80 },
                (false, true) => [22, 163, 100],
                (true, false) => [34, 91, 224],
                _ => [151, 71, 255]
            };
            var offset = (y * width + x) * 4;
            bytes[offset] = color[2]; bytes[offset + 1] = color[1]; bytes[offset + 2] = color[0]; bytes[offset + 3] = 255;
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
        result.Freeze();
        return result;
    }

    private static void CheckRenderedSignature(BitmapSource bitmap, Rect bounds, string label)
    {
        ColorAt(bitmap, bounds.X + bounds.Width * .2, bounds.Y + bounds.Height * .2, [217, 44, 80], label + " · superior izquierda");
        ColorAt(bitmap, bounds.X + bounds.Width * .8, bounds.Y + bounds.Height * .2, [22, 163, 100], label + " · superior derecha");
        ColorAt(bitmap, bounds.X + bounds.Width * .2, bounds.Y + bounds.Height * .8, [34, 91, 224], label + " · inferior izquierda");
        ColorAt(bitmap, bounds.X + bounds.Width * .8, bounds.Y + bounds.Height * .8, [151, 71, 255], label + " · inferior derecha");
        ColorAt(bitmap, bounds.X + bounds.Width * .5, bounds.Y + bounds.Height * .5, PaperColor, label + " · transparencia");
    }

    private static void ColorAt(BitmapSource bitmap, double x, double y, byte[] expected, string label)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[4];
        converted.CopyPixels(new Int32Rect((int)(x * bitmap.PixelWidth), (int)(y * bitmap.PixelHeight), 1, 1), pixels, 4, 0);
        Assert(Math.Abs(pixels[2] - expected[0]) < 9 && Math.Abs(pixels[1] - expected[1]) < 9 && Math.Abs(pixels[0] - expected[2]) < 9,
            $"{label}: RGB esperado {string.Join(',', expected)}, recibido {pixels[2]},{pixels[1]},{pixels[0]}");
    }

    private static void CheckCms(string path, string expectedThumbprint, bool testTamper = false)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.Latin1.GetString(bytes);
        var range = Regex.Match(text, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        Assert(range.Success, "La firma contiene ByteRange");
        var numbers = Enumerable.Range(1, 4).Select(i => int.Parse(range.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
        Assert(numbers[0] == 0 && numbers[2] + numbers[3] == bytes.Length && numbers[1] < numbers[2], "ByteRange cubre el documento final");
        var contents = Regex.Match(text, @"/Contents\s*<([A-Fa-f0-9]+)>");
        Assert(contents.Success && contents.Groups[1].Index - 1 == numbers[1] &&
            contents.Groups[1].Index + contents.Groups[1].Length + 1 == numbers[2], "ByteRange excluye únicamente el token CMS");
        var encoded = Convert.FromHexString(contents.Groups[1].Value);
        var cmsBytes = new AsnReader(encoded, AsnEncodingRules.BER).ReadEncodedValue().ToArray();
        var signedBytes = bytes[..numbers[1]].Concat(bytes[numbers[2]..]).ToArray();
        var cms = new SignedCms(new ContentInfo(signedBytes), true);
        cms.Decode(cmsBytes);
        cms.CheckSignature(true);
        Assert(cms.SignerInfos.Count == 1 && cms.SignerInfos[0].Certificate?.Thumbprint == expectedThumbprint,
            "Firma CMS verificable con el certificado seleccionado");
        Assert(cms.SignerInfos[0].DigestAlgorithm.Value == "2.16.840.1.101.3.4.2.1", "La firma utiliza SHA-256");
        if (testTamper)
        {
            signedBytes[30] ^= 1;
            var tampered = new SignedCms(new ContentInfo(signedBytes), true);
            tampered.Decode(cmsBytes);
            var rejected = false;
            try { tampered.CheckSignature(true); }
            catch (CryptographicException) { rejected = true; }
            Assert(rejected, "La verificación rechaza un documento modificado");
        }
    }

    private static void TestDrawingAndImport()
    {
        var stroke = new Stroke(new StylusPointCollection([new StylusPoint(100, 100), new StylusPoint(170, 80),
            new StylusPoint(160, 160), new StylusPoint(240, 100)]))
        {
            DrawingAttributes = new DrawingAttributes { Color = Colors.Black, Width = 3, Height = 3, FitToCurve = true }
        };
        var image = SignatureImageService.FromStrokes(new StrokeCollection([stroke]));
        Assert(image.PixelWidth < 350 && image.PixelHeight < 250, "El dibujo se recorta alrededor de los trazos");
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Assert(pixel[3] == 0, "Los dibujos se generan con fondo transparente");
        var white = Enumerable.Repeat((byte)255, 100 * 60 * 4).ToArray();
        for (var y = 20; y < 40; y++) for (var x = 30; x < 70; x++)
        { var offset = (y * 100 + x) * 4; white[offset] = white[offset + 1] = white[offset + 2] = 0; }
        var imported = BitmapSource.Create(100, 60, 96, 96, PixelFormats.Bgra32, null, white, 400);
        var cleaned = SignatureImageService.PrepareImport(imported, true);
        Assert(cleaned.PixelWidth == 56 && cleaned.PixelHeight == 36, "La importación elimina el blanco y recorta márgenes");
        cleaned.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Assert(pixel[3] == 0, "El fondo blanco importado se vuelve transparente");
    }

    private static void TestEditObjectSnapshot()
    {
        var edit = new PageEditObject(
            Guid.NewGuid(), EditObjectKind.NewText, 0, 800, 1000,
            new Rect(0.1, 0.2, 0.4, 0.05),
            text: "Hola", fontSizeDip: 18, colorArgb: 0xFF111111,
            textMode: TextEditMode.Tap);
        var restored = edit.Snapshot().ToObject();
        Assert(restored.Text == "Hola" && restored.Kind == EditObjectKind.NewText && !restored.IsDeleted,
            "Snapshot de objeto de edición");
    }

    private static void TestContentIndexAndTextExport()
    {
        var textPdf = Path.Combine(Output, "editable_text.pdf");
        CreateSimpleTextPdf(textPdf, "Texto editable de prueba");
        var index = PdfContentIndex.Build(File.ReadAllBytes(textPdf), null);
        var hits = index.GetText(0);
        Assert(hits.Count >= 1 && hits.Any(h => h.Text.Contains("Texto editable", StringComparison.Ordinal)),
            "Indexación de texto con PdfPig");

        using var session = PdfSession.OpenAsync(textPdf).GetAwaiter().GetResult();
        var edit = new EditObjectState(Guid.NewGuid(), EditObjectKind.NewText, 0,
            session.Pages[0].WidthDip, session.Pages[0].HeightDip, new Rect(0.1, 0.7, 0.5, 0.08),
            "Inserción", 18, 0xFF111111, TextEditMode.Tap, null, null, false, null,
            "Inserción", new Rect(0.1, 0.7, 0.5, 0.08));
        var exported = Path.Combine(Output, "texto_nuevo.pdf");
        var result = new PdfExporter().ExportAsync(session, [edit], [], exported).GetAwaiter().GetResult();
        Assert(!result.UsedTapFallback, "Exportar texto nuevo sin fallback");
        var exportedIndex = PdfContentIndex.Build(File.ReadAllBytes(exported), null);
        Assert(exportedIndex.GetText(0).Any(h => h.Text.Contains("Inserción", StringComparison.Ordinal)),
            "Exportar texto nuevo");
    }

    private static void TestLegacyMigration()
    {
        var root = Path.Combine(Output, "migrate-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "legacy");
        var emptyTarget = Path.Combine(root, "empty");
        var filledTarget = Path.Combine(root, "filled");
        var custom = Path.Combine(root, "custom");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(filledTarget);
        File.WriteAllText(Path.Combine(legacy, "a.json"), "{}");
        File.WriteAllBytes(Path.Combine(legacy, "a.png"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(filledTarget, "keep.json"), "{}");
        SignatureStore.TryMigrate(legacy, emptyTarget);
        Assert(File.Exists(Path.Combine(emptyTarget, "a.json")) && File.Exists(Path.Combine(emptyTarget, "a.png")),
            "Migración copia firmas a carpeta vacía");
        SignatureStore.TryMigrate(legacy, filledTarget);
        Assert(!File.Exists(Path.Combine(filledTarget, "a.json")), "Migración omite destino no vacío");
        var threw = false;
        try { SignatureStore.TryMigrate(Path.Combine(root, "missing"), Path.Combine(root, "noop")); }
        catch { threw = true; }
        Assert(!threw, "Migración sin legacy no falla");
        var customStore = new SignatureStore(custom);
        customStore.Add("Local", SignatureKind.Imported, TestImage());
        Assert(customStore.Load().Count == 1, "Ruta custom no migra desde AppData FirmasPDF");
    }

    private static void CreateSimpleTextPdf(string path, string text)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(210);
        page.Height = XUnit.FromMillimeter(297);
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 16);
            gfx.DrawString(text, font, XBrushes.Black, new XRect(40, 80, page.Width.Point - 80, 40),
                XStringFormats.TopLeft);
        }
        document.Save(path);
    }

    private static async Task CheckWindowsCertificate(string pfxPath, string password, PdfExporter exporter, PdfSession source)
    {
        using var installed = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        try
        {
            store.Add(installed);
            var entries = CertificateService.GetWindowsCertificates();
            try
            {
                var entry = entries.Single(e => e.Certificate.Thumbprint == installed.Thumbprint);
                Assert(entry.Certificate.HasPrivateKey && entry.StoreLabel == "Usuario actual", "Selección real del almacén de Windows");
                using var signing = new SigningRequest(new X509Certificate2(entry.Certificate), "Prueba Windows", "", false);
                var destination = Path.Combine(Output, "firmado_windows.pdf");
                await exporter.ExportAsync(source, [], destination, signing);
                CheckCms(destination, installed.Thumbprint);
            }
            finally { foreach (var entry in entries) entry.Dispose(); }
        }
        finally
        {
            store.Remove(installed);
            using var key = installed.GetRSAPrivateKey();
            if (key is RSACng cng && !cng.Key.IsEphemeral) cng.Key.Delete();
            else if (key is RSACryptoServiceProvider csp) csp.PersistKeyInCsp = false;
        }
    }

    private static async Task CheckProtectedPdf(PdfExporter exporter, SignatureAsset asset)
    {
        var path = Path.Combine(Output, "protegido.pdf");
        using (var pdf = new PdfDocument())
        {
            pdf.AddPage();
            pdf.SecuritySettings.UserPassword = "lectura";
            pdf.SecuritySettings.OwnerPassword = "propietario";
            pdf.Save(path);
        }
        await ExpectThrows<Exception>(() => PdfSession.OpenAsync(path), "El PDF protegido solicita contraseña");
        using var readOnly = await PdfSession.OpenAsync(path, "lectura");
        Assert(!readOnly.CanExport, "La contraseña de lectura abre el visor con sus permisos");
        using var owner = await PdfSession.OpenAsync(path, "propietario");
        Assert(owner.CanExport, "La contraseña del propietario permite exportar");
        var page = owner.Pages[0];
        var placement = new PlacementState(Guid.NewGuid(), asset, 0, page.WidthDip, page.HeightDip, new Rect(.2, .5, .3, .1));
        var destination = Path.Combine(Output, "protegido_exportado.pdf");
        await exporter.ExportAsync(owner, [placement], destination);
        using var result = await PdfSession.OpenAsync(destination);
        Assert(result.PageCount == 1, "Exportación de un PDF abierto con su contraseña de propietario");
    }

    private static async Task CheckInterface(string original, BitmapSource image, SignatureStore store, string pfxPath, string password)
    {
        var bindingErrors = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var dialogs = new FakeDialogs();
        var window = new MainWindow(null, dialogs, store);
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveScreenshot(window, "interfaz_inicio.png");
            await window.ViewModel.OpenPathAsync(original);
            window.ViewModel.SelectedAsset = window.ViewModel.Signatures[0];
            window.ViewModel.PlaceSignature(window.ViewModel.SelectedAsset, new Point(300, 380));
            window.ViewModel.Zoom = .8;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var control = Descendants(window).OfType<SignaturePlacementControl>().Single();
            var thumbs = Descendants(control).OfType<Thumb>().ToArray();
            var originalBounds = window.ViewModel.SelectedPlacement!.Bounds;
            thumbs[0].RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            thumbs[0].RaiseEvent(new DragDeltaEventArgs(70, 30) { RoutedEvent = Thumb.DragDeltaEvent });
            thumbs[0].RaiseEvent(new DragCompletedEventArgs(70, 30, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Assert(window.ViewModel.SelectedPlacement.Bounds.X > originalBounds.X, "Arrastre de una firma desde el control WPF");
            window.ViewModel.UndoCommand.Execute(null);
            Assert(window.ViewModel.Placements.Single().Bounds == originalBounds, "Deshacer un movimiento de firma");
            window.ViewModel.RedoCommand.Execute(null);
            Assert(window.ViewModel.Placements.Single().Bounds.X > originalBounds.X, "Rehacer un movimiento de firma");
            window.ViewModel.SelectedPlacement = window.ViewModel.Placements.Single();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            control = Descendants(window).OfType<SignaturePlacementControl>().Single();
            thumbs = Descendants(control).OfType<Thumb>().ToArray();
            thumbs[1].RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            thumbs[1].RaiseEvent(new DragDeltaEventArgs(40, 20) { RoutedEvent = Thumb.DragDeltaEvent });
            thumbs[1].RaiseEvent(new DragCompletedEventArgs(40, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
            var selected = window.ViewModel.SelectedPlacement!;
            Assert(Math.Abs(selected.CanvasWidth / selected.CanvasHeight - selected.Asset.AspectRatio) < .001,
                "Redimensionado proporcional desde el control WPF");
            SaveScreenshot(window, "interfaz_documento.png");

            dialogs.Destination = Path.Combine(Output, "exportado_desde_interfaz.pdf");
            window.ViewModel.SaveCopyCommand.Execute(null);
            while (window.ViewModel.IsBusy) await Task.Delay(20);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(File.Exists(dialogs.Destination) && !window.ViewModel.HasUnsavedChanges,
                "Guardar copia desde el comando de la interfaz: " + string.Join(" | ", dialogs.Errors) + " · " + window.ViewModel.Status);

            var drawing = new SignatureDialog { Owner = window };
            drawing.Loaded += (_, _) => drawing.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                ((InkCanvas)drawing.FindName("DrawingSurface")).Strokes.Add(new Stroke(new StylusPointCollection(
                    [new StylusPoint(70, 140), new StylusPoint(220, 80), new StylusPoint(190, 165), new StylusPoint(460, 100)])));
                SaveScreenshot(drawing, "editor_firmas.png");
                Descendants(drawing).OfType<Button>().Single(b => Equals(b.Content, "Guardar firma"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            Assert(drawing.ShowDialog() == true && drawing.Draft?.Kind == SignatureKind.Drawn, "Creación de una firma desde el editor WPF");

            var importing = new SignatureDialog(image, "Firma importada") { Owner = window };
            importing.Loaded += (_, _) => importing.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                SaveScreenshot(importing, "importar_firma.png");
                Descendants(importing).OfType<Button>().Single(b => Equals(b.Content, "Guardar firma"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            Assert(importing.ShowDialog() == true && importing.Draft?.Kind == SignatureKind.Imported, "Importación desde el diálogo WPF");

            var certificateDialog = new CertificateDialog(selected.Asset.Name) { Owner = window };
            certificateDialog.Loaded += (_, _) => certificateDialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                ((TextBox)certificateDialog.FindName("FileInput")).Text = pfxPath;
                ((PasswordBox)certificateDialog.FindName("PasswordInput")).Password = password;
                Descendants(certificateDialog).OfType<Button>().Single(b => Equals(b.Content, "Revisar certificado"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                SaveScreenshot(certificateDialog, "certificado.png");
                var details = (TextBlock)certificateDialog.FindName("PfxDetails");
                Assert(details.ActualHeight >= 55, "El diálogo muestra los datos completos del certificado");
                ((Button)certificateDialog.FindName("UseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            Assert(certificateDialog.ShowDialog() == true && certificateDialog.Request != null, "Carga y selección de certificado en el diálogo WPF");
            dialogs.Signing = certificateDialog.Request;
            dialogs.Destination = Path.Combine(Output, "firmado_desde_interfaz.pdf");
            window.ViewModel.SignDigitallyCommand.Execute(null);
            while (window.ViewModel.IsBusy) await Task.Delay(20);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(File.Exists(dialogs.Destination) && dialogs.Errors.Count == 0, "Firma digital desde la interfaz");
            using (var pdf = PdfReader.Open(dialogs.Destination, PdfDocumentOpenMode.Import))
                Assert(PdfSession.ContainsDigitalSignature(pdf), "El PDF guardado por la interfaz contiene firma digital");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(bindingErrors.Errors.Count == 0, "La interfaz WPF no presenta errores de enlace de datos: " + string.Join(" | ", bindingErrors.Errors));
        }
        finally
        {
            window.Close();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
        }
    }

    private static void SaveScreenshot(Window window, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        using var output = File.Create(Path.Combine(Output, name));
        SignatureImageService.SavePng(image, output);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FALLO: " + message);
        _checks++;
        Console.WriteLine("OK · " + message);
    }
    private static async Task ExpectThrows<T>(Func<Task> operation, string message) where T : Exception
    {
        var thrown = false;
        try { await operation(); }
        catch (T) { thrown = true; }
        Assert(thrown, message);
    }
    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
    private sealed class FakeDialogs : IDialogService
    {
        public string? Destination { get; set; }
        public SigningRequest? Signing { get; set; }
        public List<string> Errors { get; } = [];
        public string? PickPdf() => null;
        public string? PickImage() => null;
        public string? PickDestination(string name) => Destination;
        public SignatureDraft? DrawSignature() => null;
        public SignatureDraft? ImportSignature(string path) => null;
        public string? AskName(string name) => null;
        public string? AskPdfPassword() => null;
        public SigningRequest? ChooseCertificate(string? name) => Signing;
        public bool Confirm(string title, string message, string acceptLabel) => true;
        public void ShowError(string message) => Errors.Add(message);
    }
}
