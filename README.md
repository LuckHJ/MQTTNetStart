# 小橘桌宠

这是一个常驻桌面的 WPF 桌宠。左键拖动桌宠移动位置，右键打开菜单，可选择“拍照上传...”进入摄像头预览、抓拍、保存和上传流程，或打开紧凑的数独小游戏。

上传接口为 `http://localhost:8080/api/updateFaceFeature`，以 multipart/form-data 的 `file` 字段发送 JPEG 图片。

摄像头开启后会使用项目根目录的 `best.onnx` 实时检测并框选人脸。模型会自动复制到程序输出目录；部署时请确保 `best.onnx` 与程序一起发布。当前检测器适配 Ultralytics YOLO 导出的图像检测 ONNX 模型。