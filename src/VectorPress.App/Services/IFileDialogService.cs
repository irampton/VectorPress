namespace VectorPress.App.Services;

public interface IFileDialogService
{
    Task<string?> PickSvgFileAsync();
}
