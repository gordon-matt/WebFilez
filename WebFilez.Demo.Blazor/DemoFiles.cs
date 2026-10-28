using System.IO.Compression;
using Heroicsoft.NetForge.Drawing;

namespace WebFilez.Demo;

/// <summary>Fills the demo's folders with sample files the first time a demo runs.</summary>
public static class DemoFiles
{
    public static void Seed(string contentRoot)
    {
        SeedFiles(Path.Combine(contentRoot, "App_Data", "files"));
        SeedMedia(Path.Combine(contentRoot, "App_Data", "media"));
        SeedHandbook(Path.Combine(contentRoot, "App_Data", "handbook"));
    }

    private static void SeedFiles(string root)
    {
        if (Directory.Exists(root))
        {
            return;
        }

        Write(root, "Welcome.md", """
            # Welcome to WebFilez

            This folder is a sandbox, so try anything:

            - Drag files from your desktop to upload them, or drop a whole folder.
            - Right-click for more actions, or press F2 to rename and Delete to delete.
            - Drag items onto a folder, in the listing or the tree, to move them. Hold Ctrl to copy.
            - Double-click a text file to edit it, a photo to edit it in FileRobot, a Word document to edit it in SuperDoc, or a spreadsheet to edit it in Fortune Sheet. Right-click a photo and choose Preview to look without changing it.
            """);

        Write(root, "Documents/Meeting notes.txt", """
            Planning meeting, 3 March

            - Ship the file manager as a NuGet package.
            - Replace the old file manager in ArcaFS with it.
            - Revisit sharing links after version 1.
            """);
        Write(root, "Documents/Budget 2026.csv", """
            Month,Income,Spend
            January,12000,9800
            February,11800,10150
            March,12500,9900
            """);
        Write(root, "Documents/Drafts/Announcement.html", "<h1>Coming soon</h1>\n<p>Not served inline: HTML is always downloaded.</p>\n");

        Write(root, "Projects/website/index.js", "export function greet(name) {\n    return `Hello, ${name}!`;\n}\n");
        Write(root, "Projects/website/styles.css", "body {\n    font-family: system-ui, sans-serif;\n}\n");
        Write(root, "Projects/website/package.json", "{\n  \"name\": \"website\",\n  \"version\": \"1.0.0\"\n}\n");
        Write(root, "Projects/notes.json", "{\n  \"todo\": [\"Write tests\", \"Ship it\"]\n}\n");

        CreateImage(Path.Combine(root, "Photos", "Sunrise.jpg"), 1600, 1067, "#f97316", "#7c3aed");
        CreateImage(Path.Combine(root, "Photos", "Ocean.jpg"), 1600, 1067, "#0ea5e9", "#1e3a8a");
        CreateImage(Path.Combine(root, "Photos", "Forest.png"), 1200, 1200, "#84cc16", "#065f46");

        using (var zip = ZipFile.Open(Path.Combine(root, "Projects", "website-backup.zip"), ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Path.Combine(root, "Projects", "website", "index.js"), "website/index.js");
            zip.CreateEntryFromFile(Path.Combine(root, "Projects", "website", "styles.css"), "website/styles.css");
        }

        Directory.CreateDirectory(Path.Combine(root, "Empty folder"));
    }

    private static void SeedMedia(string root)
    {
        if (Directory.Exists(root))
        {
            return;
        }

        CreateImage(Path.Combine(root, "banners", "Spring sale.jpg"), 1920, 640, "#ec4899", "#f59e0b");
        CreateImage(Path.Combine(root, "banners", "New arrivals.jpg"), 1920, 640, "#14b8a6", "#4f46e5");
        CreateImage(Path.Combine(root, "products", "Mug.png"), 800, 800, "#fde68a", "#b45309");
        CreateImage(Path.Combine(root, "products", "T-shirt.png"), 800, 800, "#bfdbfe", "#1d4ed8");
        CreateImage(Path.Combine(root, "products", "Poster.webp"), 900, 1200, "#ddd6fe", "#6d28d9");
        CreateImage(Path.Combine(root, "logo.png"), 512, 512, "#4f46e5", "#06b6d4");
        Write(root, "README.txt", "Images in this folder have public URLs under /media, so the picker can hand them to a CMS field.\n");
    }

    private static void SeedHandbook(string root)
    {
        if (Directory.Exists(root))
        {
            return;
        }

        Write(root, "Read me first.txt", "This root is read-only. You can browse, preview and download, but not change anything.\n");
        Write(root, "Policies/Holidays.md", "# Holidays\n\nBook time off at least two weeks ahead.\n");
        Write(root, "Policies/Expenses.md", "# Expenses\n\nKeep receipts and submit them by the end of the month.\n");
        CreateImage(Path.Combine(root, "Office map.png"), 1400, 900, "#e2e8f0", "#64748b");
    }

    private static void Write(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void CreateImage(string path, int width, int height, string fromHex, string toHex)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var start = ParseHex(fromHex);
        var end = ParseHex(toHex);
        var bgra = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float t = ((x / (float)width) + (y / (float)height)) / 2;
                int offset = ((y * width) + x) * 4;
                bgra[offset] = (byte)(start.B + ((end.B - start.B) * t));
                bgra[offset + 1] = (byte)(start.G + ((end.G - start.G) * t));
                bgra[offset + 2] = (byte)(start.R + ((end.R - start.R) * t));
                bgra[offset + 3] = 255;
            }
        }

        RasterFormat format = ImageFormats.FromExtension(Path.GetExtension(path));
        if (format == RasterFormat.Unknown)
        {
            format = RasterFormat.Png;
        }

        var processed = new ImageProcessor().EncodeBgraAsync(
            bgra,
            width,
            height,
            new ImageEncode(format)).GetAwaiter().GetResult();

        File.WriteAllBytes(path, processed.Bytes);
    }

    private static (byte R, byte G, byte B) ParseHex(string hex)
    {
        string value = hex.Trim().TrimStart('#');
        return (
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16));
    }
}