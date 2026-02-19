using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;

namespace InkDrawGen.Helpers
{
    internal static class FolderPickerService
    {
        internal static async Task PickOutputFolderAsync(MainPage page)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".csv");

            var folder = await picker.PickSingleFolderAsync().AsTask();
            if (folder is null) return;

            var tb = page.FindName("OutputFolderTextBox") as TextBox;
            if (tb == null) return;
            tb.Text = folder.Path;
        }

        internal static async Task<StorageFile> PickCsvAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".csv");

            return await picker.PickSingleFileAsync().AsTask();
        }
    }
}
