using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using CV = OpenCvSharp;

namespace CabinetFaceWpf;

internal sealed class FaceDetector : IDisposable
{
    private const float ScoreThreshold = 0.35f;
    private const float NmsThreshold = 0.45f;
    private const int MaxCandidatesBeforeNms = 300;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly bool _channelsLast;

    public FaceDetector(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"未找到人脸检测模型：{modelPath}。请将 best.onnx 放在程序目录中。", modelPath);
        }

        _session = new InferenceSession(modelPath);
        var input = _session.InputMetadata.First();
        _inputName = input.Key;

        int[] dimensions = input.Value.Dimensions;
        _channelsLast = dimensions.Length == 4 && dimensions[^1] == 3;
        if (dimensions.Length != 4 || (!_channelsLast && dimensions[1] != 3))
        {
            _session.Dispose();
            throw new InvalidDataException("模型输入必须为 NCHW 或 NHWC 格式的四维图像张量。");
        }

        _inputHeight = _channelsLast ? dimensions[1] : dimensions[2];
        _inputWidth = _channelsLast ? dimensions[2] : dimensions[3];
        if (_inputWidth <= 0 || _inputHeight <= 0)
        {
            _inputWidth = 640;
            _inputHeight = 640;
        }
    }

    public void DrawDetections(CV.Mat frame)
    {
        var input = CreateInput(frame, out float scale, out int padLeft, out int padTop);
        using var results = _session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(_inputName, input)
        });

        var output = results.First().AsTensor<float>();
        int[] dimensions = output.Dimensions.ToArray();
        if (dimensions.Length < 2)
        {
            throw new InvalidDataException("模型输出不是可识别的人脸检测张量。");
        }

        int secondLast = dimensions[^2];
        int last = dimensions[^1];
        bool featuresFirst = secondLast < last;
        int featureCount = featuresFirst ? secondLast : last;
        int candidateCount = featuresFirst ? last : secondLast;
        if (featureCount < 5)
        {
            throw new InvalidDataException($"模型检测输出只有 {featureCount} 个特征，无法解析人脸框。");
        }

        float[] values = output.ToArray();
        var boxes = new List<CV.Rect>();
        var scores = new List<float>();
        float maxCoordinate = 0;

        for (int candidate = 0; candidate < candidateCount; candidate++)
        {
            float centerX = GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 0);
            float centerY = GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 1);
            float width = GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 2);
            float height = GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 3);
            if (!float.IsFinite(centerX) || !float.IsFinite(centerY) ||
                !float.IsFinite(width) || !float.IsFinite(height)) continue;

            maxCoordinate = Math.Max(maxCoordinate,
                Math.Max(Math.Max(Math.Abs(centerX), Math.Abs(centerY)),
                    Math.Max(Math.Abs(width), Math.Abs(height))));
        }

        float coordinateScaleX = maxCoordinate <= 2 ? _inputWidth : 1;
        float coordinateScaleY = maxCoordinate <= 2 ? _inputHeight : 1;
        for (int candidate = 0; candidate < candidateCount; candidate++)
        {
            float centerX = GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 0);
            float centerY = GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 1);
            float width = GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 2);
            float height = GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 3);
            if (!float.IsFinite(centerX) || !float.IsFinite(centerY) ||
                !float.IsFinite(width) || !float.IsFinite(height)) continue;

            float confidence;
            if (featureCount == 6)
            {
                confidence =
                    GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 4) *
                    GetValue(values, featuresFirst, candidateCount, featureCount, candidate, 5);
            }
            else
            {
                confidence = 0;
                for (int feature = 4; feature < featureCount; feature++)
                {
                    confidence = Math.Max(confidence,
                        GetValue(values, featuresFirst, candidateCount, featureCount, candidate, feature));
                }
            }

            if (!float.IsFinite(confidence) || confidence < ScoreThreshold) continue;

            float left = (centerX * coordinateScaleX - width * coordinateScaleX / 2 - padLeft) / scale;
            float top = (centerY * coordinateScaleY - height * coordinateScaleY / 2 - padTop) / scale;
            float right = (centerX * coordinateScaleX + width * coordinateScaleX / 2 - padLeft) / scale;
            float bottom = (centerY * coordinateScaleY + height * coordinateScaleY / 2 - padTop) / scale;

            int x1 = Math.Clamp((int)Math.Floor(left), 0, frame.Width);
            int y1 = Math.Clamp((int)Math.Floor(top), 0, frame.Height);
            int x2 = Math.Clamp((int)Math.Ceiling(right), 0, frame.Width);
            int y2 = Math.Clamp((int)Math.Ceiling(bottom), 0, frame.Height);
            if (x2 <= x1 || y2 <= y1) continue;

            boxes.Add(new CV.Rect(x1, y1, x2 - x1, y2 - y1));
            scores.Add(confidence);
        }

        if (boxes.Count == 0) return;

        var selected = new List<int>();
        foreach (int candidate in Enumerable.Range(0, boxes.Count)
                     .OrderByDescending(index => scores[index])
                     .Take(MaxCandidatesBeforeNms))
        {
            if (selected.All(index => IntersectionOverUnion(boxes[candidate], boxes[index]) <= NmsThreshold))
            {
                selected.Add(candidate);
            }
        }

        foreach (int index in selected)
        {
            CV.Rect box = boxes[index];
            CV.Cv2.Rectangle(frame, box, new CV.Scalar(0, 255, 0), 2);
            CV.Cv2.PutText(frame, $"{scores[index]:P0}",
                new CV.Point(box.X, Math.Max(box.Y - 6, 16)),
                CV.HersheyFonts.HersheySimplex, 0.55, new CV.Scalar(0, 255, 0), 2);
        }
    }

    private static float IntersectionOverUnion(CV.Rect first, CV.Rect second)
    {
        int intersectionWidth = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        int intersectionHeight = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        int intersection = intersectionWidth * intersectionHeight;
        int union = first.Width * first.Height + second.Width * second.Height - intersection;
        return union == 0 ? 0 : (float)intersection / union;
    }

    private DenseTensor<float> CreateInput(CV.Mat frame, out float scale, out int padLeft, out int padTop)
    {
        scale = Math.Min((float)_inputWidth / frame.Width, (float)_inputHeight / frame.Height);
        int resizedWidth = (int)Math.Round(frame.Width * scale);
        int resizedHeight = (int)Math.Round(frame.Height * scale);
        padLeft = (_inputWidth - resizedWidth) / 2;
        padTop = (_inputHeight - resizedHeight) / 2;

        using var resized = frame.Resize(new CV.Size(resizedWidth, resizedHeight));
        using var padded = new CV.Mat(_inputHeight, _inputWidth, CV.MatType.CV_8UC3,
            new CV.Scalar(114, 114, 114));
        using (var roi = new CV.Mat(padded, new CV.Rect(padLeft, padTop, resizedWidth, resizedHeight)))
        {
            resized.CopyTo(roi);
        }

        using var rgb = new CV.Mat();
        CV.Cv2.CvtColor(padded, rgb, CV.ColorConversionCodes.BGR2RGB);
        using var normalized = new CV.Mat();
        rgb.ConvertTo(normalized, CV.MatType.CV_32FC3, 1.0 / 255.0);

        var tensor = _channelsLast
            ? new DenseTensor<float>(new[] { 1, _inputHeight, _inputWidth, 3 })
            : new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });

        for (int y = 0; y < _inputHeight; y++)
        {
            for (int x = 0; x < _inputWidth; x++)
            {
                CV.Vec3f pixel = normalized.At<CV.Vec3f>(y, x);
                if (_channelsLast)
                {
                    tensor[0, y, x, 0] = pixel.Item0;
                    tensor[0, y, x, 1] = pixel.Item1;
                    tensor[0, y, x, 2] = pixel.Item2;
                }
                else
                {
                    tensor[0, 0, y, x] = pixel.Item0;
                    tensor[0, 1, y, x] = pixel.Item1;
                    tensor[0, 2, y, x] = pixel.Item2;
                }
            }
        }

        return tensor;
    }

    private static float GetValue(
        float[] values, bool featuresFirst, int candidateCount, int featureCount, int candidate, int feature)
    {
        return featuresFirst
            ? values[feature * candidateCount + candidate]
            : values[candidate * featureCount + feature];
    }

    public void Dispose() => _session.Dispose();
}
