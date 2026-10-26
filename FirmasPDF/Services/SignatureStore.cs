using System.IO;
using System.Text.Json;
using FirmasPDF.Models;
using System.Windows.Media.Imaging;

namespace FirmasPDF.Services;

public sealed class SignatureStore
{
    public string FolderPath { get; }
    public int SkippedItems { get; private set; }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SignatureStore(string? folderPath = null) => FolderPath = folderPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FirmasPDF", "Signatures");

    public IReadOnlyList<SignatureAsset> Load()
    {
        Directory.CreateDirectory(FolderPath);
        var assets = new List<SignatureAsset>();
        SkippedItems = 0;
        foreach (var path in Directory.EnumerateFiles(FolderPath, "*.json"))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<SignatureMetadata>(File.ReadAllText(path))
                    ?? throw new JsonException("Metadatos vacíos.");
                if (Path.GetFileNameWithoutExtension(path) != metadata.Id.ToString("N") ||
                    string.IsNullOrWhiteSpace(metadata.Name)) throw new JsonException("Metadatos inválidos.");
                assets.Add(new SignatureAsset(metadata.Id, metadata.Name, metadata.Kind, metadata.CreatedAt,
                    SignatureImageService.Load(ImagePath(metadata.Id))));
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or
                NotSupportedException or InvalidOperationException)
            {
                SkippedItems++;
            }
        }
        return assets.OrderByDescending(s => s.CreatedAt).ToArray();
    }

    public SignatureAsset Add(string name, SignatureKind kind, BitmapSource image)
    {
        name = ValidateName(name);
        Directory.CreateDirectory(FolderPath);
        var id = Guid.NewGuid();
        var asset = new SignatureAsset(id, name, kind, DateTimeOffset.Now, image);
        var temp = ImagePath(id) + ".tmp";
        try
        {
            File.WriteAllBytes(temp, asset.PngBytes);
            File.Move(temp, ImagePath(id));
            WriteMetadata(asset, name);
            return asset;
        }
        catch
        {
            File.Delete(temp);
            File.Delete(ImagePath(id));
            throw;
        }
    }

    public void Rename(SignatureAsset asset, string name)
    {
        name = ValidateName(name);
        WriteMetadata(asset, name);
        asset.Name = name;
    }

    public void Delete(SignatureAsset asset)
    {
        // Metadata is the commit record; remove it after the image to avoid a half-deleted entry.
        var image = ImagePath(asset.Id);
        var tombstone = image + ".deleted";
        File.Move(image, tombstone, true);
        try { File.Delete(MetadataPath(asset.Id)); }
        catch { File.Move(tombstone, image, true); throw; }
        File.Delete(tombstone);
    }

    private void WriteMetadata(SignatureAsset asset, string name)
    {
        var path = MetadataPath(asset.Id);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(
                new SignatureMetadata(asset.Id, name, asset.Kind, asset.CreatedAt), JsonOptions));
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }

    private static string ValidateName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > 80)
            throw new InvalidOperationException("El nombre debe tener entre 1 y 80 caracteres.");
        return trimmed;
    }
    private string ImagePath(Guid id) => Path.Combine(FolderPath, id.ToString("N") + ".png");
    private string MetadataPath(Guid id) => Path.Combine(FolderPath, id.ToString("N") + ".json");
    private sealed record SignatureMetadata(Guid Id, string Name, SignatureKind Kind, DateTimeOffset CreatedAt);
}
