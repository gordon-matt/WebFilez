using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
            - Double-click a text file to edit it, or a photo to edit it in FileRobot. Right-click a photo and choose Preview to look without changing it.
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

        CreateImage(Path.Combine(root, "Photos", "Sunrise.jpg"), 1600, 1067, Color.ParseHex("#f97316"), Color.ParseHex("#7c3aed"));
        CreateImage(Path.Combine(root, "Photos", "Ocean.jpg"), 1600, 1067, Color.ParseHex("#0ea5e9"), Color.ParseHex("#1e3a8a"));
        CreateImage(Path.Combine(root, "Photos", "Forest.png"), 1200, 1200, Color.ParseHex("#84cc16"), Color.ParseHex("#065f46"));

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

        CreateImage(Path.Combine(root, "banners", "Spring sale.jpg"), 1920, 640, Color.ParseHex("#ec4899"), Color.ParseHex("#f59e0b"));
        CreateImage(Path.Combine(root, "banners", "New arrivals.jpg"), 1920, 640, Color.ParseHex("#14b8a6"), Color.ParseHex("#4f46e5"));
        CreateImage(Path.Combine(root, "products", "Mug.png"), 800, 800, Color.ParseHex("#fde68a"), Color.ParseHex("#b45309"));
        CreateImage(Path.Combine(root, "products", "T-shirt.png"), 800, 800, Color.ParseHex("#bfdbfe"), Color.ParseHex("#1d4ed8"));
        CreateImage(Path.Combine(root, "products", "Poster.webp"), 900, 1200, Color.ParseHex("#ddd6fe"), Color.ParseHex("#6d28d9"));
        CreateImage(Path.Combine(root, "logo.png"), 512, 512, Color.ParseHex("#4f46e5"), Color.ParseHex("#06b6d4"));
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
        CreateImage(Path.Combine(root, "Office map.png"), 1400, 900, Color.ParseHex("#e2e8f0"), Color.ParseHex("#64748b"));
    }

    private static void Write(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void CreateImage(string path, int width, int height, Color from, Color to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var start = from.ToPixel<Rgba32>();
        var end = to.ToPixel<Rgba32>();

        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    float t = ((x / (float)width) + (y / (float)height)) / 2;
                    row[x] = new Rgba32(
                        (byte)(start.R + ((end.R - start.R) * t)),
                        (byte)(start.G + ((end.G - start.G) * t)),
                        (byte)(start.B + ((end.B - start.B) * t)));
                }
            }
        });

        image.Save(path);
    }
}