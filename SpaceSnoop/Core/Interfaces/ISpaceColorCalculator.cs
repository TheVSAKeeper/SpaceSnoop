namespace SpaceSnoop.Core.Interfaces;

public interface ISpaceColorCalculator
{
    /// <summary>
    ///     Текущая интенсивность цвета (насколько выражен цвет в зависимости от размера).
    /// </summary>
    int Intensity { get; set; }

    /// <summary>
    ///     Получает цвет на основе размера директории.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно получить цвет.</param>
    /// <param name="maxSize">Максимальный размер директории.</param>
    /// <returns>Цвет, соответствующий размеру директории.</returns>
    Color GetColorBasedOnSize(DirectorySpace directory, long maxSize);

    /// <summary>
    ///     Получает цвет на основе размера файла.
    /// </summary>
    /// <param name="file">Файл, для которого нужно получить цвет.</param>
    /// <param name="maxSize">Максимальный размер файла.</param>
    /// <returns>Цвет, соответствующий размеру файла.</returns>
    Color GetColorBasedOnSize(FileSpace file, long maxSize);
}
