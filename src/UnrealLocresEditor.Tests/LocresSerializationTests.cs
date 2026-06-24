using System.IO;
using UnrealLocresEditor.Utils;
using Xunit;

namespace UnrealLocresEditor.Tests;

public class LocresSerializationTests
{
    [Fact]
    public void ComposeAndParseDisplayKey_RoundTrips()
    {
        var rootKey = LocresFileData.ComposeDisplayKey(string.Empty, "RootKey");
        Assert.Equal("RootKey", rootKey);
        Assert.Equal((string.Empty, "RootKey"), LocresFileData.ParseDisplayKey(rootKey));

        var namespacedKey = LocresFileData.ComposeDisplayKey("UI", "Button.Ok");
        Assert.Equal("UI/Button.Ok", namespacedKey);
        Assert.Equal(("UI", "Button.Ok"), LocresFileData.ParseDisplayKey(namespacedKey));
    }

    [Fact]
    public void LocresRoundTrip_PreservesNamespacesEntriesAndTranslations()
    {
        var locres = new LocresFileData
        {
            Version = LocresVersion.CityHash,
        };

        var rootNamespace = new LocresNamespaceData { Name = string.Empty };
        rootNamespace.Entries.Add(new LocresEntryData
        {
            NamespaceName = string.Empty,
            Key = "RootKey",
            Translation = "Root Text",
            SourceHash = LocresCrc32.StrCrc32("Root Source"),
        });
        locres.Namespaces.Add(rootNamespace);

        var uiNamespace = new LocresNamespaceData { Name = "UI" };
        uiNamespace.Entries.Add(new LocresEntryData
        {
            NamespaceName = "UI",
            Key = "Button.Ok",
            Translation = "Добре",
            SourceHash = LocresCrc32.StrCrc32("OK"),
        });
        locres.Namespaces.Add(uiNamespace);

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.locres");
        try
        {
            locres.Write(tempPath);
            var reloaded = LocresFileData.Read(tempPath);

            Assert.Equal(LocresVersion.CityHash, reloaded.Version);
            Assert.Equal(2, reloaded.Namespaces.Count);

            var rootReloaded = reloaded.Namespaces[0];
            Assert.Equal(string.Empty, rootReloaded.Name);
            Assert.Single(rootReloaded.Entries);
            Assert.Equal("RootKey", rootReloaded.Entries[0].Key);
            Assert.Equal("Root Text", rootReloaded.Entries[0].Translation);

            var uiReloaded = reloaded.Namespaces[1];
            Assert.Equal("UI", uiReloaded.Name);
            Assert.Single(uiReloaded.Entries);
            Assert.Equal("Button.Ok", uiReloaded.Entries[0].Key);
            Assert.Equal("Добре", uiReloaded.Entries[0].Translation);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
