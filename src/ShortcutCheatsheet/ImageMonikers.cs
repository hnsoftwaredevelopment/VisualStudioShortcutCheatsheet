using System;
using Microsoft.VisualStudio.Imaging.Interop;

namespace ShortcutCheatsheet
{
    /// <summary>Our own images, defined in ShortcutCheatsheet.imagemanifest. The GUID and ID must match that file and the .vsct.</summary>
    internal static class ImageMonikers
    {
        public const string GuidString = "0603114e-3612-40be-b924-b8b033ebb6d2";
        public const int CheatsheetId = 1;

        public static readonly ImageMoniker Cheatsheet = new ImageMoniker { Guid = new Guid(GuidString), Id = CheatsheetId };
    }
}
