using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var report = new List<string>();
var verifyUrls = args.Contains("--verify-urls", StringComparer.Ordinal);
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
byte[] Read(ZipArchiveEntry entry) { using var source = entry.Open(); using var memory = new MemoryStream(); source.CopyTo(memory); return memory.ToArray(); }
using var package = ZipFile.OpenRead(Path.Combine(root, "artifacts/packages/Zamay.2.0.0.nupkg"));
using var symbols = ZipFile.OpenRead(Path.Combine(root, "artifacts/packages/Zamay.2.0.0.snupkg"));
foreach (var entry in package.Entries.OrderBy(e => e.FullName)) {
    report.Add($"{entry.FullName} ({entry.Length} bytes)");
    Require(!entry.FullName.Contains(".."), "Unsafe archive path");
    Require(!entry.FullName.Split('/').Any(p => new[] { "bin", "obj", "tests", "samples", "artifacts" }.Contains(p, StringComparer.OrdinalIgnoreCase)), "Unexpected build/development content");
    Require(!new[] { ".db", ".pfx", ".user", ".suo", ".pdb" }.Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase), "Unexpected package file");
}
foreach (var required in new[] { "README.md", "LICENSE", "icon.png", "Zamay.nuspec" }) Require(package.GetEntry(required) != null, "Missing " + required);
var nuspec = XDocument.Load(new MemoryStream(Read(package.GetEntry("Zamay.nuspec")!)));
XNamespace ns = nuspec.Root!.Name.Namespace;
var metadata = nuspec.Root.Element(ns + "metadata")!;
Require((string?)metadata.Element(ns + "id") == "Zamay", "Wrong ID");
Require((string?)metadata.Element(ns + "version") == "2.0.0", "Wrong version");
Require((string?)metadata.Element(ns + "authors") == "ZamaySolves", "Wrong authors");
Require((string?)metadata.Element(ns + "license") == "MIT", "Wrong license");
Require((string?)metadata.Element(ns + "readme") == "README.md", "README not declared");
Require((string?)metadata.Element(ns + "icon") == "icon.png", "Icon not declared");
Require(metadata.Descendants(ns + "dependency").All(d => (string?)d.Attribute("id") == "Microsoft.Data.Sqlite"), "Unexpected package dependency");
Require(metadata.Descendants(ns + "dependency").Count() == 2, "Expected provider dependency for both TFMs");
var repository = metadata.Element(ns + "repository");
var url = (string?)repository?.Attribute("url");
var commit = (string?)repository?.Attribute("commit");
Require(commit?.Length == 40, "Missing source commit metadata");
Require(string.IsNullOrEmpty(url) || Uri.IsWellFormedUriString(url, UriKind.Absolute), "Invalid repository URL");
Require(!verifyUrls || !string.IsNullOrEmpty(url), "Remote Source Link verification requires the real RepositoryUrl and a publicly available source commit.");
report.Add($"Repository URL: {(string.IsNullOrEmpty(url) ? "PENDING owner configuration" : url)}; commit: {commit}");
var dlls = package.Entries.Where(e => e.FullName.EndsWith("/Zamay.dll", StringComparison.Ordinal)).ToArray();
Require(dlls.Length == 2, "Expected exactly two target assemblies");
foreach (var dll in dlls) {
    using var pe = new PEReader(new MemoryStream(Read(dll)));
    var md = pe.GetMetadataReader();
    Require(md.GetString(md.GetAssemblyDefinition().Name) == "Zamay", "Wrong assembly name");
    var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
    var windows = dll.FullName.Contains("windows", StringComparison.Ordinal);
    Require(windows == refs.Contains("System.Windows.Forms"), "Windows dependency leaked across TFMs");
    foreach (var type in md.TypeDefinitions) {
        var definition = md.GetTypeDefinition(type);
        var typeNamespace = md.GetString(definition.Namespace);
        Require(!typeNamespace.StartsWith("ObjectDisplay", StringComparison.Ordinal) && !typeNamespace.StartsWith("ZamaySolves", StringComparison.Ordinal), "Legacy namespace present");
    }
    var xmlPath = dll.FullName[..^4] + ".xml";
    Require(package.GetEntry(xmlPath) != null, "Missing XML docs");
    var xml = Encoding.UTF8.GetString(Read(package.GetEntry(xmlPath)!));
    Require(xml.Contains("Zamay.Core.ObjectInspector", StringComparison.Ordinal), "Inspection XML docs missing");
    var pdbPath = dll.FullName[..^4] + ".pdb";
    var pdbEntry = symbols.GetEntry(pdbPath);
    Require(pdbEntry != null, "Missing portable PDB");
    using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(Read(pdbEntry!)));
    var pdb = provider.GetMetadataReader();
    var names = pdb.Documents.Select(h => pdb.GetString(pdb.GetDocument(h).Name)).ToArray();
    Require(names.All(n => !n.Contains(":\\", StringComparison.Ordinal) && !n.Contains("/Users/", StringComparison.OrdinalIgnoreCase) && !n.Contains("/home/", StringComparison.Ordinal)), "Developer absolute path in PDB");
    int embedded = 0; string? sourceLink = null;
    foreach (var h in pdb.CustomDebugInformation) {
        var info = pdb.GetCustomDebugInformation(h); var kind = pdb.GetGuid(info.Kind);
        if (kind == new Guid("0e8a571b-6926-466e-b4ad-8ab04611f5fe")) embedded++;
        if (kind == new Guid("cc110556-a091-4d38-9fec-25ab9a351a6a")) sourceLink = Encoding.UTF8.GetString(pdb.GetBlobBytes(info.Value));
    }
    Require(embedded > 0, "No embedded sources");
    if (!string.IsNullOrEmpty(url)) {
        Require(sourceLink is not null, "Configured repository has no Source Link metadata");
        using var json = JsonDocument.Parse(sourceLink!);
        Require(json.RootElement.GetProperty("documents").EnumerateObject().Any(), "Empty Source Link mappings");
        if (verifyUrls) {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            foreach (var handle in pdb.Documents) {
                var document = pdb.GetDocument(handle);
                var path = pdb.GetString(document.Name).Replace('\\', '/');
                if (path.Contains("/obj/", StringComparison.Ordinal)) continue;
                string? sourceUrl = null;
                foreach (var mapping in json.RootElement.GetProperty("documents").EnumerateObject()) {
                    var prefix = mapping.Name.TrimEnd('*');
                    if (path.StartsWith(prefix, StringComparison.Ordinal)) {
                        sourceUrl = mapping.Value.GetString()!.Replace("*", path[prefix.Length..]);
                        break;
                    }
                }
                Require(sourceUrl is not null, "Unmapped source document: " + path);
                var bytes = await client.GetByteArrayAsync(sourceUrl!);
                var expected = pdb.GetBlobBytes(document.Hash);
                var actual = System.Security.Cryptography.SHA256.HashData(bytes);
                Require(expected.SequenceEqual(actual), "Published source does not match the PDB checksum: " + path);
            }
            report.Add("Remote source URLs and SHA256 document checksums: PASS");
        }
    }
    report.Add($"{dll.FullName}: assembly and XML PASS; portable PDB PASS; embedded sources={embedded}; Source Link={(string.IsNullOrEmpty(url) ? "PENDING URL" : "metadata present")}");
}
foreach(var entry in package.Entries.Where(e => e.FullName.EndsWith(".md",StringComparison.Ordinal))) {
    var text=Encoding.UTF8.GetString(Read(entry));
    foreach(var marker in new[]{"ChatGPT","OpenAI","Claude","Codex","AI-generated","generated by AI","C:\\Users\\"}) Require(!text.Contains(marker,StringComparison.OrdinalIgnoreCase),"Unexpected development text in "+entry.FullName);
}
report.Add("Package ID, version, license, authors, icon, README, assemblies, dependencies, XML and symbols: PASS");
report.Add(string.IsNullOrEmpty(url) ? "Ready to publish: NO. Set the real repository URL, publish its source commit, and rebuild/re-audit Source Link." : "Repository metadata configured. Verify Source Link URLs resolve to the published source commit before publishing.");
File.WriteAllLines(Path.Combine(root,"artifacts/package-content.txt"),report);
Console.WriteLine(string.Join(Environment.NewLine,report));


