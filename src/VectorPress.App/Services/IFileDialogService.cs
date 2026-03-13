namespace VectorPress.App.Services;

public interface IFileDialogService
{
    Task<string?> PickSvgFileAsync();

    Task<string?> PickStlSavePathAsync(string suggestedFileName);
}
