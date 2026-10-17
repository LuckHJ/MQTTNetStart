using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;

namespace CabinetFaceWpf;

internal sealed class AnimatedGif : IDisposable
{
    private readonly FileStream _stream;
    private readonly GifBitmapDecoder _decoder;

    public AnimatedGif(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"未找到桌宠动作 GIF：{path}", path);
        }

        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            _decoder = new GifBitmapDecoder(
                _stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnDemand);
            if (_decoder.Frames.Count == 0)
            {
                throw new InvalidDataException($"GIF 文件中没有动画帧：{path}");
            }
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public IList<BitmapFrame> Frames => _decoder.Frames;

    public TimeSpan GetFrameDuration(int frameIndex)
    {
        const string delayQuery = "/grctlext/Delay";
        BitmapFrame frame = _decoder.Frames[frameIndex];
        if (frame.Metadata is BitmapMetadata metadata &&
            metadata.ContainsQuery(delayQuery) &&
            metadata.GetQuery(delayQuery) is IConvertible delayValue)
        {
            int hundredthsOfASecond = Convert.ToInt32(delayValue, CultureInfo.InvariantCulture);
            if (hundredthsOfASecond > 0)
            {
                return TimeSpan.FromMilliseconds(Math.Max(20, hundredthsOfASecond * 10));
            }
        }

        return TimeSpan.FromMilliseconds(100);
    }

    public void Dispose() => _stream.Dispose();
}
