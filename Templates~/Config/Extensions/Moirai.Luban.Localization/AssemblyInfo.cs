using Luban;

// Luban 启动时扫描 exe 同目录下文件名含 "Luban" 的 dll 并加载，
// 带 [RegisterBehaviour] 的程序集内的 [Pipeline]/[OutputSaver] 等行为会被注册。
[assembly: RegisterBehaviour]
