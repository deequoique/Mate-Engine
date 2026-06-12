# Voices Folder

把可回放的预录台词放在这个目录下。

建议命名方式：

- `head_*.wav`
- `body_*.wav`
- `tail_*.wav`
- `idle_*.wav`

当前 `PetRemote/VoiceLibrary.cs` 会递归扫描这里的 `.wav` / `.mp3` / `.ogg` 文件，并把它们以本机 `file:///...` URL 的形式返回给 A 端。
