using System;
using System.Reflection;
using Luban;
using Moirai.Atropos;
using SimpleJSON;
using UnityEngine;
using Moirai.Atropos.Resource;

namespace Moirai.GameProto.Config
{
	/// <summary>
	/// 配置加载器。桥接 Luban 生成代码与资源系统。
	/// </summary>
	[Serializable]
	public sealed partial class LubanHandler
	{
		private static LubanHandler s_Instance;
		public static LubanHandler Instance => s_Instance ??= new LubanHandler();

		private const string CONFIG_PATH = "Assets/AssetRaw/Default/Config/Table/";

		private Tables _tables;
		/// <summary>
		/// 所有配置表。
		/// </summary>
		public Tables Tables
		{
			get
			{
				_tables ??= Load();
				return _tables;
			}
		}

		/// <summary>
		/// 加载配置。
		/// <remarks>自动判断加载bin或json配置</remarks>
		/// </summary>
		private Tables Load()
		{
			ConstructorInfo tablesCtor = typeof(Tables).GetConstructors()[0];
			Type loaderReturnType = tablesCtor.GetParameters()[0].ParameterType.GetGenericArguments()[1];
			// 根据 Tables 的构造函数的 Loader 的返回值类型决定使用 json 还是 ByteBuf Loader
			System.Delegate loader = loaderReturnType == typeof(ByteBuf)
				? new System.Func<string, ByteBuf>(LoadByteBuf)
				: (System.Delegate)new System.Func<string, JSONNode>(LoadJson);

			// 表是逐张懒读的：这里建出的对象还不会碰任何资源，因此建不成只可能是反射对不上
			// 或构造器自己抛。失败一律不落状态，下次访问照常重来
			if (tablesCtor.Invoke(new object[] { loader }) is not Tables tables)
			{
				throw new GameException(StringUtility.Format(
					"Failed to construct '{0}' via its constructor '{1}'.",
					typeof(Tables).FullName, tablesCtor));
			}

			return tables;
		}

		/// <summary>
		/// 按当前生成路线装载一张独立表（不走 Tables）。
		/// <remarks>路线写在转表配置里（bin 或 json），落到代码上就是生成表的构造器收 ByteBuf 还是 JSONNode；
		/// 与 <see cref="Load"/> 同一个判据，所以换 --format=json 不需要改任何读取代码。</remarks>
		/// </summary>
		/// <param name="relativePath">相对 CONFIG_PATH 的路径，不含扩展名</param>
		internal static T LoadTable<T>(string relativePath) where T : class
		{
			ConstructorInfo tableCtor = typeof(T).GetConstructors()[0];
			Type bufferType = tableCtor.GetParameters()[0].ParameterType;
			object buffer = bufferType == typeof(ByteBuf)
				? (object)LoadByteBufFrom(relativePath)
				: LoadJsonFrom(relativePath);

			if (tableCtor.Invoke(new[] { buffer }) is not T table)
			{
				throw new GameException(StringUtility.Format(
					"Failed to construct '{0}' via its constructor '{1}'.",
					typeof(T).FullName, tableCtor));
			}

			return table;
		}

		/// <summary>
		/// 加载二进制配置。
		/// </summary>
		/// <param name="file">FileName</param>
		/// <returns>ByteBuf</returns>
		private static ByteBuf LoadByteBuf(string file)
		{
			return LoadByteBufFrom(file);
		}

		/// <summary>
		/// 从 CONFIG_PATH 下的相对路径加载二进制配置。多语言按语言子目录分份导出后走这一层。
		/// </summary>
		/// <param name="relativePath">相对 CONFIG_PATH 的路径，不含扩展名</param>
		/// <returns>ByteBuf</returns>
		private static ByteBuf LoadByteBufFrom(string relativePath)
		{
			LogUtility.Info("Load bin config: {0}.bytes", relativePath);
			TextAsset textAsset = LoadTextAsset(CONFIG_PATH + relativePath + ".bytes");
			byte[] bytes = textAsset.bytes;
			return new ByteBuf(bytes);
		}

		/// <summary>
		/// 从文件中加载 json 配置。
		/// </summary>
		/// <param name="file"></param>
		/// <returns></returns>
		private static JSONNode LoadJson(string file)
		{
			return LoadJsonFrom(file);
		}

		/// <summary>
		/// 从 CONFIG_PATH 下的相对路径加载 json 配置。与 <see cref="LoadByteBufFrom"/> 对称，
		/// 供 <see cref="LoadTable{T}"/> 在 json 路线下按语言子目录取表。
		/// </summary>
		/// <param name="relativePath">相对 CONFIG_PATH 的路径，不含扩展名</param>
		/// <returns>JSONNode</returns>
		private static JSONNode LoadJsonFrom(string relativePath)
		{
			LogUtility.Info("Load json config: {0}.json", relativePath);
			TextAsset textAsset = LoadTextAsset(CONFIG_PATH + relativePath + ".json");
			string json = textAsset.text;
			return JSON.Parse(json);
		}

		/// <summary>
		/// 加载配置文本资源。
		/// </summary>
		/// <param name="location"></param>
		/// <returns></returns>
		private static TextAsset LoadTextAsset(string location)
		{
#if UNITY_EDITOR
			if (!Application.isPlaying)
			{
				// 非播放态（编辑器预览、转表）不经资源系统，直读资产库；取不到同样要点名，
				// 让 null 回到调用方就变成几行开外一句无来由的 NRE
				var fromDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(location);
				if (fromDatabase == null)
				{
					throw new GameException(StringUtility.Format(
						"Config asset is missing: '{0}'. Generate config first.", location));
				}

				return fromDatabase;
			}
#endif
			// 因为配置是预加载（Asset tag 为 PRELOAD），所以无需异步加载
			using var lease = ResourceService.LoadLease<TextAsset>(location);
			if (lease.Asset == null)
			{
				// 预加载完成前同步取不到（如启动早期读表）：显式报错而非让 null 穿透成 NRE。
				// 表数据未落成状态，下一次读表照常重试
				throw new GameException(StringUtility.Format(
					"Config asset is not loadable yet: '{0}'. Config tables are PRELOAD-tagged; " +
					"make sure the resource preload has finished before reading tables.",
					location));
			}

			return lease.Asset;
		}
	}
}