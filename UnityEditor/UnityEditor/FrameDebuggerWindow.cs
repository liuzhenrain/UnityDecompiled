using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor
{
	internal class FrameDebuggerWindow : EditorWindow
	{
		internal class Styles
		{
			public GUIStyle header = "OL title";

			public GUIStyle entryEven = "OL EntryBackEven";

			public GUIStyle entryOdd = "OL EntryBackOdd";

			public GUIStyle rowText = "OL Label";

			public GUIStyle rowTextRight = new GUIStyle("OL Label");

			public GUIContent recordButton = new GUIContent(EditorGUIUtility.TextContent("Record|Record profiling information"));

			public GUIContent prevFrame = new GUIContent(EditorGUIUtility.IconContent("Profiler.PrevFrame", "|Go back one frame"));

			public GUIContent nextFrame = new GUIContent(EditorGUIUtility.IconContent("Profiler.NextFrame", "|Go one frame forwards"));

			public GUIContent[] headerContent;

			public readonly string[] batchBreakCauses;

			public static readonly string[] s_ColumnNames = new string[]
			{
				"#",
				"Type",
				"Vertices",
				"Indices"
			};

			public static readonly GUIContent[] mrtLabels = new GUIContent[]
			{
				EditorGUIUtility.TextContent("RT 0|Show render target #0"),
				EditorGUIUtility.TextContent("RT 1|Show render target #1"),
				EditorGUIUtility.TextContent("RT 2|Show render target #2"),
				EditorGUIUtility.TextContent("RT 3|Show render target #3"),
				EditorGUIUtility.TextContent("RT 4|Show render target #4"),
				EditorGUIUtility.TextContent("RT 5|Show render target #5"),
				EditorGUIUtility.TextContent("RT 6|Show render target #6"),
				EditorGUIUtility.TextContent("RT 7|Show render target #7")
			};

			public static readonly GUIContent depthLabel = EditorGUIUtility.TextContent("Depth|Show depth buffer");

			public static readonly GUIContent[] channelLabels = new GUIContent[]
			{
				EditorGUIUtility.TextContent("All|Show all (RGB) color channels"),
				EditorGUIUtility.TextContent("R|Show red channel only"),
				EditorGUIUtility.TextContent("G|Show green channel only"),
				EditorGUIUtility.TextContent("B|Show blue channel only"),
				EditorGUIUtility.TextContent("A|Show alpha channel only")
			};

			public static readonly GUIContent channelHeader = EditorGUIUtility.TextContent("Channels|Which render target color channels to show");

			public static readonly GUIContent levelsHeader = EditorGUIUtility.TextContent("Levels|Render target display black/white intensity levels");

			public static readonly GUIContent causeOfNewDrawCallLabel = EditorGUIUtility.TextContent("Why this draw call can't be batched with the previous one");

			public static readonly GUIContent selectShaderTooltip = EditorGUIUtility.TextContent("|Click to select shader");

			public static readonly GUIContent copyToClipboardTooltip = EditorGUIUtility.TextContent("|Click to copy shader and keywords text to clipboard.");

			public static readonly GUIContent arrayValuePopupButton = new GUIContent("...");

			public Styles()
			{
				this.rowTextRight.alignment = TextAnchor.MiddleRight;
				this.recordButton.text = "Enable";
				this.recordButton.tooltip = "Enable Frame Debugging";
				this.prevFrame.tooltip = "Previous event";
				this.nextFrame.tooltip = "Next event";
				this.headerContent = new GUIContent[FrameDebuggerWindow.Styles.s_ColumnNames.Length];
				for (int i = 0; i < this.headerContent.Length; i++)
				{
					this.headerContent[i] = EditorGUIUtility.TextContent(FrameDebuggerWindow.Styles.s_ColumnNames[i]);
				}
				this.batchBreakCauses = FrameDebuggerUtility.GetBatchBreakCauseStrings();
			}
		}

		private class ArrayValuePopup : PopupWindowContent
		{
			public delegate string GetValueStringDelegate(int index, bool highPrecision);

			private FrameDebuggerWindow.ArrayValuePopup.GetValueStringDelegate GetValueString;

			private Vector2 m_ScrollPos = Vector2.zero;

			private int m_StartIndex;

			private int m_NumValues;

			private float m_WindowWidth;

			private static readonly GUIStyle m_Style = EditorStyles.miniLabel;

			public ArrayValuePopup(int startIndex, int numValues, float windowWidth, FrameDebuggerWindow.ArrayValuePopup.GetValueStringDelegate getValueString)
			{
				this.m_StartIndex = startIndex;
				this.m_NumValues = numValues;
				this.m_WindowWidth = windowWidth;
				this.GetValueString = getValueString;
			}

			public override Vector2 GetWindowSize()
			{
				float num = FrameDebuggerWindow.ArrayValuePopup.m_Style.lineHeight + (float)FrameDebuggerWindow.ArrayValuePopup.m_Style.padding.vertical + (float)FrameDebuggerWindow.ArrayValuePopup.m_Style.margin.top;
				return new Vector2(this.m_WindowWidth, Math.Min(num * (float)this.m_NumValues, 250f));
			}

			public override void OnGUI(Rect rect)
			{
				this.m_ScrollPos = EditorGUILayout.BeginScrollView(this.m_ScrollPos, new GUILayoutOption[0]);
				for (int i = 0; i < this.m_NumValues; i++)
				{
					string text = string.Format("[{0}]\t{1}", i, this.GetValueString(this.m_StartIndex + i, false));
					GUILayout.Label(text, FrameDebuggerWindow.ArrayValuePopup.m_Style, new GUILayoutOption[0]);
				}
				EditorGUILayout.EndScrollView();
				Event current = Event.current;
				if (current.type == EventType.ContextClick && rect.Contains(current.mousePosition))
				{
					current.Use();
					string allText = string.Empty;
					for (int j = 0; j < this.m_NumValues; j++)
					{
						allText += string.Format("[{0}]\t{1}\n", j, this.GetValueString(this.m_StartIndex + j, true));
					}
					GenericMenu genericMenu = new GenericMenu();
					genericMenu.AddItem(new GUIContent("Copy value"), false, delegate
					{
						EditorGUIUtility.systemCopyBuffer = allText;
					});
					genericMenu.ShowAsContext();
				}
			}
		}

		public static readonly string[] s_FrameEventTypeNames = new string[]
		{
			"Clear (nothing)",
			"Clear (color)",
			"Clear (Z)",
			"Clear (color+Z)",
			"Clear (stencil)",
			"Clear (color+stencil)",
			"Clear (Z+stencil)",
			"Clear (color+Z+stencil)",
			"SetRenderTarget",
			"Resolve Color",
			"Resolve Depth",
			"Grab RenderTexture",
			"Static Batch",
			"Dynamic Batch",
			"Draw Mesh",
			"Draw Dynamic",
			"Draw GL",
			"GPU Skinning",
			"Draw Procedural",
			"Compute Shader",
			"Plugin Event",
			"Draw Mesh (instanced)"
		};

		private const float kScrollbarWidth = 16f;

		private const float kResizerWidth = 5f;

		private const float kMinListWidth = 200f;

		private const float kMinDetailsWidth = 200f;

		private const float kMinWindowWidth = 240f;

		private const float kDetailsMargin = 4f;

		private const float kMinPreviewSize = 64f;

		private const string kFloatFormat = "g2";

		private const string kFloatDetailedFormat = "g7";

		private const float kShaderPropertiesIndention = 15f;

		private const float kNameFieldWidth = 200f;

		private const float kValueFieldWidth = 200f;

		private const float kArrayValuePopupBtnWidth = 25f;

		private const int kShaderTypeBits = 6;

		private const int kArraySizeBitMask = 1023;

		private const int kNeedToRepaintFrames = 4;

		[SerializeField]
		private float m_ListWidth = 300f;

		private int m_RepaintFrames = 4;

		private PreviewRenderUtility m_PreviewUtility;

		public Vector2 m_PreviewDir = new Vector2(120f, -20f);

		private Material m_Material;

		private Material m_WireMaterial;

		[SerializeField]
		private TreeViewState m_TreeViewState;

		[NonSerialized]
		private FrameDebuggerTreeView m_Tree;

		[NonSerialized]
		private int m_FrameEventsHash;

		[NonSerialized]
		private int m_RTIndex;

		[NonSerialized]
		private int m_RTChannel;

		[NonSerialized]
		private float m_RTBlackLevel;

		[NonSerialized]
		private float m_RTWhiteLevel = 1f;

		private int m_PrevEventsLimit = 0;

		private int m_PrevEventsCount = 0;

		private Vector2 m_ScrollViewShaderProps = Vector2.zero;

		private ShowAdditionalInfo m_AdditionalInfo = ShowAdditionalInfo.ShaderProperties;

		private GUIContent[] m_AdditionalInfoGuiContents = (from m in Enum.GetNames(typeof(ShowAdditionalInfo))
		select new GUIContent(m)).ToArray<GUIContent>();

		private static List<FrameDebuggerWindow> s_FrameDebuggers = new List<FrameDebuggerWindow>();

		private AttachProfilerUI m_AttachProfilerUI = new AttachProfilerUI();

		private static FrameDebuggerWindow.Styles ms_Styles;

		public static FrameDebuggerWindow.Styles styles
		{
			get
			{
				FrameDebuggerWindow.Styles arg_18_0;
				if ((arg_18_0 = FrameDebuggerWindow.ms_Styles) == null)
				{
					arg_18_0 = (FrameDebuggerWindow.ms_Styles = new FrameDebuggerWindow.Styles());
				}
				return arg_18_0;
			}
		}

		public FrameDebuggerWindow()
		{
			base.position = new Rect(50f, 50f, 600f, 350f);
			base.minSize = new Vector2(400f, 200f);
		}

		[MenuItem("Window/Frame Debugger", false, 2100)]
		public static FrameDebuggerWindow ShowFrameDebuggerWindow()
		{
			FrameDebuggerWindow frameDebuggerWindow = EditorWindow.GetWindow(typeof(FrameDebuggerWindow)) as FrameDebuggerWindow;
			if (frameDebuggerWindow != null)
			{
				frameDebuggerWindow.titleContent = EditorGUIUtility.TextContent("Frame Debug");
			}
			return frameDebuggerWindow;
		}

		internal static void RepaintAll()
		{
			foreach (FrameDebuggerWindow current in FrameDebuggerWindow.s_FrameDebuggers)
			{
				current.Repaint();
			}
		}

		internal void ChangeFrameEventLimit(int newLimit)
		{
			if (newLimit > 0 && newLimit <= FrameDebuggerUtility.count)
			{
				if (newLimit != FrameDebuggerUtility.limit && newLimit > 0)
				{
					GameObject frameEventGameObject = FrameDebuggerUtility.GetFrameEventGameObject(newLimit - 1);
					if (frameEventGameObject != null)
					{
						EditorGUIUtility.PingObject(frameEventGameObject);
					}
				}
				FrameDebuggerUtility.limit = newLimit;
				if (this.m_Tree != null)
				{
					this.m_Tree.SelectFrameEventIndex(newLimit);
				}
			}
		}

		private static void DisableFrameDebugger()
		{
			if (FrameDebuggerUtility.IsLocalEnabled())
			{
				EditorApplication.SetSceneRepaintDirty();
			}
			FrameDebuggerUtility.SetEnabled(false, FrameDebuggerUtility.GetRemotePlayerGUID());
		}

		internal void OnDidOpenScene()
		{
			FrameDebuggerWindow.DisableFrameDebugger();
		}

		private void OnPlayModeStateChanged()
		{
			this.RepaintOnLimitChange();
		}

		internal void OnEnable()
		{
			base.autoRepaintOnSceneChange = true;
			FrameDebuggerWindow.s_FrameDebuggers.Add(this);
			EditorApplication.playmodeStateChanged = (EditorApplication.CallbackFunction)Delegate.Combine(EditorApplication.playmodeStateChanged, new EditorApplication.CallbackFunction(this.OnPlayModeStateChanged));
			this.m_RepaintFrames = 4;
		}

		internal void OnDisable()
		{
			if (this.m_WireMaterial != null)
			{
				UnityEngine.Object.DestroyImmediate(this.m_WireMaterial, true);
			}
			if (this.m_PreviewUtility != null)
			{
				this.m_PreviewUtility.Cleanup();
				this.m_PreviewUtility = null;
			}
			FrameDebuggerWindow.s_FrameDebuggers.Remove(this);
			EditorApplication.playmodeStateChanged = (EditorApplication.CallbackFunction)Delegate.Remove(EditorApplication.playmodeStateChanged, new EditorApplication.CallbackFunction(this.OnPlayModeStateChanged));
			FrameDebuggerWindow.DisableFrameDebugger();
		}

		public void EnableIfNeeded()
		{
			if (!FrameDebuggerUtility.IsLocalEnabled() && !FrameDebuggerUtility.IsRemoteEnabled())
			{
				this.m_RTChannel = 0;
				this.m_RTIndex = 0;
				this.m_RTBlackLevel = 0f;
				this.m_RTWhiteLevel = 1f;
				this.ClickEnableFrameDebugger();
				this.RepaintOnLimitChange();
			}
		}

		private void ClickEnableFrameDebugger()
		{
			bool flag = FrameDebuggerUtility.IsLocalEnabled() || FrameDebuggerUtility.IsRemoteEnabled();
			bool flag2 = !flag && this.m_AttachProfilerUI.IsEditor();
			if (!flag2 || FrameDebuggerUtility.locallySupported)
			{
				if (flag2)
				{
					if (EditorApplication.isPlaying && !EditorApplication.isPaused)
					{
						EditorApplication.isPaused = true;
					}
				}
				if (!flag)
				{
					FrameDebuggerUtility.SetEnabled(true, ProfilerDriver.connectedProfiler);
				}
				else
				{
					FrameDebuggerUtility.SetEnabled(false, FrameDebuggerUtility.GetRemotePlayerGUID());
				}
				if (FrameDebuggerUtility.IsLocalEnabled())
				{
					GameView gameView = (GameView)WindowLayout.FindEditorWindowOfType(typeof(GameView));
					if (gameView)
					{
						gameView.ShowTab();
					}
				}
				this.m_PrevEventsLimit = FrameDebuggerUtility.limit;
				this.m_PrevEventsCount = FrameDebuggerUtility.count;
			}
		}

		private bool DrawToolbar(FrameDebuggerEvent[] descs)
		{
			bool result = false;
			bool flag = !this.m_AttachProfilerUI.IsEditor() || FrameDebuggerUtility.locallySupported;
			GUILayout.BeginHorizontal(EditorStyles.toolbar, new GUILayoutOption[0]);
			EditorGUI.BeginChangeCheck();
			using (new EditorGUI.DisabledScope(!flag))
			{
				GUILayout.Toggle(FrameDebuggerUtility.IsLocalEnabled() || FrameDebuggerUtility.IsRemoteEnabled(), FrameDebuggerWindow.styles.recordButton, EditorStyles.toolbarButton, new GUILayoutOption[]
				{
					GUILayout.MinWidth(80f)
				});
			}
			if (EditorGUI.EndChangeCheck())
			{
				this.ClickEnableFrameDebugger();
				result = true;
			}
			this.m_AttachProfilerUI.OnGUILayout(this);
			bool flag2 = FrameDebuggerUtility.IsLocalEnabled() || FrameDebuggerUtility.IsRemoteEnabled();
			if (flag2 && ProfilerDriver.connectedProfiler != FrameDebuggerUtility.GetRemotePlayerGUID())
			{
				FrameDebuggerUtility.SetEnabled(false, FrameDebuggerUtility.GetRemotePlayerGUID());
				FrameDebuggerUtility.SetEnabled(true, ProfilerDriver.connectedProfiler);
			}
			GUI.enabled = flag2;
			EditorGUI.BeginChangeCheck();
			int num;
			using (new EditorGUI.DisabledScope(FrameDebuggerUtility.count <= 1))
			{
				num = EditorGUILayout.IntSlider(FrameDebuggerUtility.limit, 1, FrameDebuggerUtility.count, new GUILayoutOption[0]);
			}
			if (EditorGUI.EndChangeCheck())
			{
				this.ChangeFrameEventLimit(num);
			}
			GUILayout.Label(" of " + FrameDebuggerUtility.count, EditorStyles.miniLabel, new GUILayoutOption[0]);
			using (new EditorGUI.DisabledScope(num <= 1))
			{
				if (GUILayout.Button(FrameDebuggerWindow.styles.prevFrame, EditorStyles.toolbarButton, new GUILayoutOption[0]))
				{
					this.ChangeFrameEventLimit(num - 1);
				}
			}
			using (new EditorGUI.DisabledScope(num >= FrameDebuggerUtility.count))
			{
				if (GUILayout.Button(FrameDebuggerWindow.styles.nextFrame, EditorStyles.toolbarButton, new GUILayoutOption[0]))
				{
					this.ChangeFrameEventLimit(num + 1);
				}
				if (this.m_PrevEventsLimit == this.m_PrevEventsCount)
				{
					if (FrameDebuggerUtility.count != this.m_PrevEventsCount && FrameDebuggerUtility.limit == this.m_PrevEventsLimit)
					{
						this.ChangeFrameEventLimit(FrameDebuggerUtility.count);
					}
				}
				this.m_PrevEventsLimit = FrameDebuggerUtility.limit;
				this.m_PrevEventsCount = FrameDebuggerUtility.count;
			}
			GUILayout.EndHorizontal();
			return result;
		}

		private void DrawMeshPreview(FrameDebuggerEventData curEventData, Rect previewRect, Rect meshInfoRect, Mesh mesh, int meshSubset)
		{
			if (this.m_PreviewUtility == null)
			{
				this.m_PreviewUtility = new PreviewRenderUtility();
				this.m_PreviewUtility.camera.fieldOfView = 30f;
			}
			if (this.m_Material == null)
			{
				this.m_Material = (EditorGUIUtility.GetBuiltinExtraResource(typeof(Material), "Default-Material.mat") as Material);
			}
			if (this.m_WireMaterial == null)
			{
				this.m_WireMaterial = ModelInspector.CreateWireframeMaterial();
			}
			this.m_PreviewUtility.BeginPreview(previewRect, "preBackground");
			ModelInspector.RenderMeshPreview(mesh, this.m_PreviewUtility, this.m_Material, this.m_WireMaterial, this.m_PreviewDir, meshSubset);
			this.m_PreviewUtility.EndAndDrawPreview(previewRect);
			string text = mesh.name;
			if (string.IsNullOrEmpty(text))
			{
				text = "<no name>";
			}
			string text2 = string.Concat(new object[]
			{
				text,
				" subset ",
				meshSubset,
				"\n",
				curEventData.vertexCount,
				" verts, ",
				curEventData.indexCount,
				" indices"
			});
			if (curEventData.instanceCount > 1)
			{
				string text3 = text2;
				text2 = string.Concat(new object[]
				{
					text3,
					", ",
					curEventData.instanceCount,
					" instances"
				});
			}
			EditorGUI.DropShadowLabel(meshInfoRect, text2);
		}

		private bool DrawEventMesh(FrameDebuggerEventData curEventData)
		{
			Mesh mesh = curEventData.mesh;
			bool result;
			if (mesh == null)
			{
				result = false;
			}
			else
			{
				Rect rect = GUILayoutUtility.GetRect(10f, 10f, new GUILayoutOption[]
				{
					GUILayout.ExpandHeight(true)
				});
				if (rect.width < 64f || rect.height < 64f)
				{
					result = true;
				}
				else
				{
					GameObject frameEventGameObject = FrameDebuggerUtility.GetFrameEventGameObject(curEventData.frameEventIndex);
					Rect rect2 = rect;
					rect2.yMin = rect2.yMax - EditorGUIUtility.singleLineHeight * 2f;
					Rect position = rect2;
					rect2.xMin = rect2.center.x;
					position.xMax = position.center.x;
					if (Event.current.type == EventType.MouseDown)
					{
						if (rect2.Contains(Event.current.mousePosition))
						{
							EditorGUIUtility.PingObject(mesh);
							Event.current.Use();
						}
						if (frameEventGameObject != null && position.Contains(Event.current.mousePosition))
						{
							EditorGUIUtility.PingObject(frameEventGameObject.GetInstanceID());
							Event.current.Use();
						}
					}
					this.m_PreviewDir = PreviewGUI.Drag2D(this.m_PreviewDir, rect);
					if (Event.current.type == EventType.Repaint)
					{
						int meshSubset = curEventData.meshSubset;
						this.DrawMeshPreview(curEventData, rect, rect2, mesh, meshSubset);
						if (frameEventGameObject != null)
						{
							EditorGUI.DropShadowLabel(position, frameEventGameObject.name);
						}
					}
					result = true;
				}
			}
			return result;
		}

		private void DrawRenderTargetControls(FrameDebuggerEventData cur)
		{
			if (cur.rtWidth > 0 && cur.rtHeight > 0)
			{
				bool disabled = cur.rtFormat == 1 || cur.rtFormat == 3;
				bool flag = cur.rtHasDepthTexture != 0;
				short num = cur.rtCount;
				if (flag)
				{
					num += 1;
				}
				EditorGUILayout.LabelField("RenderTarget", cur.rtName, new GUILayoutOption[0]);
				GUILayout.BeginHorizontal(EditorStyles.toolbar, new GUILayoutOption[0]);
				EditorGUI.BeginChangeCheck();
				bool flag2;
				using (new EditorGUI.DisabledScope(num <= 1))
				{
					GUIContent[] array = new GUIContent[(int)num];
					for (int i = 0; i < (int)cur.rtCount; i++)
					{
						array[i] = FrameDebuggerWindow.Styles.mrtLabels[i];
					}
					if (flag)
					{
						array[(int)cur.rtCount] = FrameDebuggerWindow.Styles.depthLabel;
					}
					int num2 = Mathf.Clamp(this.m_RTIndex, 0, (int)(num - 1));
					flag2 = (num2 != this.m_RTIndex);
					this.m_RTIndex = num2;
					this.m_RTIndex = EditorGUILayout.Popup(this.m_RTIndex, array, EditorStyles.toolbarPopup, new GUILayoutOption[]
					{
						GUILayout.Width(70f)
					});
				}
				GUILayout.Space(10f);
				using (new EditorGUI.DisabledScope(disabled))
				{
					GUILayout.Label(FrameDebuggerWindow.Styles.channelHeader, EditorStyles.miniLabel, new GUILayoutOption[0]);
					this.m_RTChannel = GUILayout.Toolbar(this.m_RTChannel, FrameDebuggerWindow.Styles.channelLabels, EditorStyles.toolbarButton, new GUILayoutOption[0]);
				}
				GUILayout.Space(10f);
				GUILayout.Label(FrameDebuggerWindow.Styles.levelsHeader, EditorStyles.miniLabel, new GUILayoutOption[0]);
				EditorGUILayout.MinMaxSlider(ref this.m_RTBlackLevel, ref this.m_RTWhiteLevel, 0f, 1f, new GUILayoutOption[]
				{
					GUILayout.MaxWidth(200f)
				});
				if (EditorGUI.EndChangeCheck() || flag2)
				{
					Vector4 channels = Vector4.zero;
					if (this.m_RTChannel == 1)
					{
						channels.x = 1f;
					}
					else if (this.m_RTChannel == 2)
					{
						channels.y = 1f;
					}
					else if (this.m_RTChannel == 3)
					{
						channels.z = 1f;
					}
					else if (this.m_RTChannel == 4)
					{
						channels.w = 1f;
					}
					else
					{
						channels = Vector4.one;
					}
					int num3 = this.m_RTIndex;
					if (num3 >= (int)cur.rtCount)
					{
						num3 = -1;
					}
					FrameDebuggerUtility.SetRenderTargetDisplayOptions(num3, channels, this.m_RTBlackLevel, this.m_RTWhiteLevel);
					this.RepaintAllNeededThings();
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.Label(string.Format("{0}x{1} {2}", cur.rtWidth, cur.rtHeight, (RenderTextureFormat)cur.rtFormat), new GUILayoutOption[0]);
				if (cur.rtDim == 4)
				{
					GUILayout.Label("Rendering into cubemap", new GUILayoutOption[0]);
				}
				if (cur.rtFormat == 3 && SystemInfo.graphicsDeviceVersion.StartsWith("Direct3D 9"))
				{
					EditorGUILayout.HelpBox("Rendering into shadowmap on DX9, can't visualize it in the game view properly", MessageType.Info, true);
				}
			}
		}

		private void DrawEventDrawCallInfo(FrameDebuggerEventData curEventData)
		{
			string text = curEventData.shaderName + ", pass #" + curEventData.shaderPassIndex;
			EditorGUILayout.LabelField("Shader", text, new GUILayoutOption[0]);
			if (GUI.Button(GUILayoutUtility.GetLastRect(), FrameDebuggerWindow.Styles.selectShaderTooltip, GUI.skin.label))
			{
				EditorGUIUtility.PingObject(curEventData.shaderInstanceID);
				Event.current.Use();
			}
			if (!string.IsNullOrEmpty(curEventData.shaderKeywords))
			{
				EditorGUILayout.LabelField("Keywords", curEventData.shaderKeywords, new GUILayoutOption[0]);
				if (GUI.Button(GUILayoutUtility.GetLastRect(), FrameDebuggerWindow.Styles.copyToClipboardTooltip, GUI.skin.label))
				{
					EditorGUIUtility.systemCopyBuffer = text + Environment.NewLine + curEventData.shaderKeywords;
				}
			}
			this.DrawStates(curEventData);
			if (curEventData.batchBreakCause > 1)
			{
				GUILayout.Space(10f);
				GUILayout.Label(FrameDebuggerWindow.Styles.causeOfNewDrawCallLabel, EditorStyles.boldLabel, new GUILayoutOption[0]);
				GUILayout.Label(FrameDebuggerWindow.styles.batchBreakCauses[curEventData.batchBreakCause], EditorStyles.wordWrappedLabel, new GUILayoutOption[0]);
			}
			GUILayout.Space(15f);
			this.m_AdditionalInfo = (ShowAdditionalInfo)GUILayout.Toolbar((int)this.m_AdditionalInfo, this.m_AdditionalInfoGuiContents, new GUILayoutOption[0]);
			ShowAdditionalInfo additionalInfo = this.m_AdditionalInfo;
			if (additionalInfo != ShowAdditionalInfo.Preview)
			{
				if (additionalInfo == ShowAdditionalInfo.ShaderProperties)
				{
					this.DrawShaderProperties(curEventData.shaderProperties);
				}
			}
			else if (!this.DrawEventMesh(curEventData))
			{
				EditorGUILayout.LabelField("Vertices", curEventData.vertexCount.ToString(), new GUILayoutOption[0]);
				EditorGUILayout.LabelField("Indices", curEventData.indexCount.ToString(), new GUILayoutOption[0]);
			}
		}

		private void DrawEventComputeDispatchInfo(FrameDebuggerEventData curEventData)
		{
			EditorGUILayout.LabelField("Compute Shader", curEventData.csName, new GUILayoutOption[0]);
			if (GUI.Button(GUILayoutUtility.GetLastRect(), GUIContent.none, GUI.skin.label))
			{
				EditorGUIUtility.PingObject(curEventData.csInstanceID);
				Event.current.Use();
			}
			EditorGUILayout.LabelField("Kernel", curEventData.csKernel, new GUILayoutOption[0]);
			string label;
			if (curEventData.csThreadGroupsX != 0 || curEventData.csThreadGroupsY != 0 || curEventData.csThreadGroupsZ != 0)
			{
				label = string.Format("{0}x{1}x{2}", curEventData.csThreadGroupsX, curEventData.csThreadGroupsY, curEventData.csThreadGroupsZ);
			}
			else
			{
				label = "indirect dispatch";
			}
			EditorGUILayout.LabelField("Thread Groups", label, new GUILayoutOption[0]);
		}

		private void DrawCurrentEvent(Rect rect, FrameDebuggerEvent[] descs)
		{
			int num = FrameDebuggerUtility.limit - 1;
			if (num >= 0 && num < descs.Length)
			{
				GUILayout.BeginArea(rect);
				FrameDebuggerEvent frameDebuggerEvent = descs[num];
				FrameDebuggerEventData frameDebuggerEventData;
				bool frameEventData = FrameDebuggerUtility.GetFrameEventData(num, out frameDebuggerEventData);
				if (frameEventData)
				{
					this.DrawRenderTargetControls(frameDebuggerEventData);
				}
				GUILayout.Label(string.Format("Event #{0}: {1}", num + 1, FrameDebuggerWindow.s_FrameEventTypeNames[(int)frameDebuggerEvent.type]), EditorStyles.boldLabel, new GUILayoutOption[0]);
				if (FrameDebuggerUtility.IsRemoteEnabled() && FrameDebuggerUtility.receivingRemoteFrameEventData)
				{
					GUILayout.Label("Receiving frame event data...", new GUILayoutOption[0]);
				}
				else if (frameEventData)
				{
					if (frameDebuggerEventData.vertexCount > 0 || frameDebuggerEventData.indexCount > 0)
					{
						this.DrawEventDrawCallInfo(frameDebuggerEventData);
					}
					else if (frameDebuggerEvent.type == FrameEventType.ComputeDispatch)
					{
						this.DrawEventComputeDispatchInfo(frameDebuggerEventData);
					}
				}
				GUILayout.EndArea();
			}
		}

		private void DrawShaderPropertyFlags(int flags)
		{
			string text = string.Empty;
			if ((flags & 2) != 0)
			{
				text += 'v';
			}
			if ((flags & 4) != 0)
			{
				text += 'f';
			}
			if ((flags & 8) != 0)
			{
				text += 'g';
			}
			if ((flags & 16) != 0)
			{
				text += 'h';
			}
			if ((flags & 32) != 0)
			{
				text += 'd';
			}
			GUILayout.Label(text, EditorStyles.miniLabel, new GUILayoutOption[]
			{
				GUILayout.MinWidth(20f)
			});
		}

		private void ShaderPropertyCopyValueMenu(Rect valueRect, object value)
		{
			Event current = Event.current;
			if (current.type == EventType.ContextClick && valueRect.Contains(current.mousePosition))
			{
				current.Use();
				GenericMenu genericMenu = new GenericMenu();
				genericMenu.AddItem(new GUIContent("Copy value"), false, delegate
				{
					string systemCopyBuffer = string.Empty;
					if (value is Vector4)
					{
						systemCopyBuffer = ((Vector4)value).ToString("g7");
					}
					else if (value is Matrix4x4)
					{
						systemCopyBuffer = ((Matrix4x4)value).ToString("g7");
					}
					else if (value is float)
					{
						systemCopyBuffer = ((float)value).ToString("g7");
					}
					else
					{
						systemCopyBuffer = value.ToString();
					}
					EditorGUIUtility.systemCopyBuffer = systemCopyBuffer;
				});
				genericMenu.ShowAsContext();
			}
		}

		private void OnGUIShaderPropFloats(ShaderFloatInfo[] floats, int startIndex, int numValues)
		{
			GUILayout.BeginHorizontal(new GUILayoutOption[0]);
			GUILayout.Space(15f);
			ShaderFloatInfo shaderFloatInfo = floats[startIndex];
			if (numValues == 1)
			{
				GUILayout.Label(shaderFloatInfo.name, EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.DrawShaderPropertyFlags(shaderFloatInfo.flags);
				GUILayout.Label(shaderFloatInfo.value.ToString("g2"), EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.ShaderPropertyCopyValueMenu(GUILayoutUtility.GetLastRect(), shaderFloatInfo.value);
			}
			else
			{
				string text = string.Format("{0} [{1}]", shaderFloatInfo.name, numValues);
				GUILayout.Label(text, EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.DrawShaderPropertyFlags(shaderFloatInfo.flags);
				Rect rect = GUILayoutUtility.GetRect(FrameDebuggerWindow.Styles.arrayValuePopupButton, GUI.skin.button, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				rect.width = 25f;
				if (GUI.Button(rect, FrameDebuggerWindow.Styles.arrayValuePopupButton))
				{
					FrameDebuggerWindow.ArrayValuePopup.GetValueStringDelegate getValueString = (int index, bool highPrecision) => floats[index].value.ToString((!highPrecision) ? "g2" : "g7");
					PopupWindowWithoutFocus.Show(rect, new FrameDebuggerWindow.ArrayValuePopup(startIndex, numValues, 100f, getValueString), new PopupLocationHelper.PopupLocation[]
					{
						PopupLocationHelper.PopupLocation.Left,
						PopupLocationHelper.PopupLocation.Below,
						PopupLocationHelper.PopupLocation.Right
					});
				}
			}
			GUILayout.EndHorizontal();
		}

		private void OnGUIShaderPropVectors(ShaderVectorInfo[] vectors, int startIndex, int numValues)
		{
			GUILayout.BeginHorizontal(new GUILayoutOption[0]);
			GUILayout.Space(15f);
			ShaderVectorInfo shaderVectorInfo = vectors[startIndex];
			if (numValues == 1)
			{
				GUILayout.Label(shaderVectorInfo.name, EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.DrawShaderPropertyFlags(shaderVectorInfo.flags);
				GUILayout.Label(shaderVectorInfo.value.ToString("g2"), EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.ShaderPropertyCopyValueMenu(GUILayoutUtility.GetLastRect(), shaderVectorInfo.value);
			}
			else
			{
				string text = string.Format("{0} [{1}]", shaderVectorInfo.name, numValues);
				GUILayout.Label(text, EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.DrawShaderPropertyFlags(shaderVectorInfo.flags);
				Rect rect = GUILayoutUtility.GetRect(FrameDebuggerWindow.Styles.arrayValuePopupButton, GUI.skin.button, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				rect.width = 25f;
				if (GUI.Button(rect, FrameDebuggerWindow.Styles.arrayValuePopupButton))
				{
					FrameDebuggerWindow.ArrayValuePopup.GetValueStringDelegate getValueString = (int index, bool highPrecision) => vectors[index].value.ToString((!highPrecision) ? "g2" : "g7");
					PopupWindowWithoutFocus.Show(rect, new FrameDebuggerWindow.ArrayValuePopup(startIndex, numValues, 200f, getValueString), new PopupLocationHelper.PopupLocation[]
					{
						PopupLocationHelper.PopupLocation.Left,
						PopupLocationHelper.PopupLocation.Below,
						PopupLocationHelper.PopupLocation.Right
					});
				}
			}
			GUILayout.EndHorizontal();
		}

		private void OnGUIShaderPropMatrices(ShaderMatrixInfo[] matrices, int startIndex, int numValues)
		{
			GUILayout.BeginHorizontal(new GUILayoutOption[0]);
			GUILayout.Space(15f);
			ShaderMatrixInfo shaderMatrixInfo = matrices[startIndex];
			if (numValues == 1)
			{
				GUILayout.Label(shaderMatrixInfo.name, EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.DrawShaderPropertyFlags(shaderMatrixInfo.flags);
				GUILayout.Label(shaderMatrixInfo.value.ToString("g2"), EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.ShaderPropertyCopyValueMenu(GUILayoutUtility.GetLastRect(), shaderMatrixInfo.value);
			}
			else
			{
				string text = string.Format("{0} [{1}]", shaderMatrixInfo.name, numValues);
				GUILayout.Label(text, EditorStyles.miniLabel, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				this.DrawShaderPropertyFlags(shaderMatrixInfo.flags);
				Rect rect = GUILayoutUtility.GetRect(FrameDebuggerWindow.Styles.arrayValuePopupButton, GUI.skin.button, new GUILayoutOption[]
				{
					GUILayout.MinWidth(200f)
				});
				rect.width = 25f;
				if (GUI.Button(rect, FrameDebuggerWindow.Styles.arrayValuePopupButton))
				{
					FrameDebuggerWindow.ArrayValuePopup.GetValueStringDelegate getValueString = (int index, bool highPrecision) => '\n' + matrices[index].value.ToString((!highPrecision) ? "g2" : "g7");
					PopupWindowWithoutFocus.Show(rect, new FrameDebuggerWindow.ArrayValuePopup(startIndex, numValues, 200f, getValueString), new PopupLocationHelper.PopupLocation[]
					{
						PopupLocationHelper.PopupLocation.Left,
						PopupLocationHelper.PopupLocation.Below,
						PopupLocationHelper.PopupLocation.Right
					});
				}
			}
			GUILayout.EndHorizontal();
		}

		private void OnGUIShaderPropBuffer(ShaderBufferInfo t)
		{
			GUILayout.BeginHorizontal(new GUILayoutOption[0]);
			GUILayout.Space(15f);
			GUILayout.Label(t.name, EditorStyles.miniLabel, new GUILayoutOption[]
			{
				GUILayout.MinWidth(200f)
			});
			this.DrawShaderPropertyFlags(t.flags);
			GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.miniLabel, new GUILayoutOption[]
			{
				GUILayout.MinWidth(200f)
			});
			GUILayout.EndHorizontal();
		}

		private void OnGUIShaderPropTexture(ShaderTextureInfo t)
		{
			GUILayout.BeginHorizontal(new GUILayoutOption[0]);
			GUILayout.Space(15f);
			GUILayout.Label(t.name, EditorStyles.miniLabel, new GUILayoutOption[]
			{
				GUILayout.MinWidth(200f)
			});
			this.DrawShaderPropertyFlags(t.flags);
			Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.label, new GUILayoutOption[]
			{
				GUILayout.MinWidth(200f)
			});
			Event current = Event.current;
			Rect position = rect;
			position.width = position.height;
			if (t.value != null && position.Contains(current.mousePosition))
			{
				GUI.Label(position, GUIContent.Temp(string.Empty, "Ctrl + Click to show preview"));
			}
			if (current.type == EventType.Repaint)
			{
				Rect position2 = rect;
				position2.xMin += position.width;
				if (t.value != null)
				{
					Texture texture = t.value;
					if (texture.dimension != TextureDimension.Tex2D)
					{
						texture = AssetPreview.GetMiniThumbnail(texture);
					}
					EditorGUI.DrawPreviewTexture(position, texture);
				}
				GUI.Label(position2, (!(t.value != null)) ? t.textureName : t.value.name);
			}
			else if (current.type == EventType.MouseDown)
			{
				if (rect.Contains(current.mousePosition))
				{
					EditorGUI.PingObjectOrShowPreviewOnClick(t.value, rect);
					current.Use();
				}
			}
			GUILayout.EndHorizontal();
		}

		private void DrawShaderProperties(ShaderProperties props)
		{
			this.m_ScrollViewShaderProps = GUILayout.BeginScrollView(this.m_ScrollViewShaderProps, new GUILayoutOption[0]);
			if (props.textures.Count<ShaderTextureInfo>() > 0)
			{
				GUILayout.Label("Textures", EditorStyles.boldLabel, new GUILayoutOption[0]);
				ShaderTextureInfo[] textures = props.textures;
				for (int i = 0; i < textures.Length; i++)
				{
					ShaderTextureInfo t = textures[i];
					this.OnGUIShaderPropTexture(t);
				}
			}
			if (props.floats.Count<ShaderFloatInfo>() > 0)
			{
				GUILayout.Label("Floats", EditorStyles.boldLabel, new GUILayoutOption[0]);
				int num;
				for (int j = 0; j < props.floats.Length; j += num)
				{
					num = (props.floats[j].flags >> 6 & 1023);
					this.OnGUIShaderPropFloats(props.floats, j, num);
				}
			}
			if (props.vectors.Count<ShaderVectorInfo>() > 0)
			{
				GUILayout.Label("Vectors", EditorStyles.boldLabel, new GUILayoutOption[0]);
				int num2;
				for (int k = 0; k < props.vectors.Length; k += num2)
				{
					num2 = (props.vectors[k].flags >> 6 & 1023);
					this.OnGUIShaderPropVectors(props.vectors, k, num2);
				}
			}
			if (props.matrices.Count<ShaderMatrixInfo>() > 0)
			{
				GUILayout.Label("Matrices", EditorStyles.boldLabel, new GUILayoutOption[0]);
				int num3;
				for (int l = 0; l < props.matrices.Length; l += num3)
				{
					num3 = (props.matrices[l].flags >> 6 & 1023);
					this.OnGUIShaderPropMatrices(props.matrices, l, num3);
				}
			}
			if (props.buffers.Count<ShaderBufferInfo>() > 0)
			{
				GUILayout.Label("Buffers", EditorStyles.boldLabel, new GUILayoutOption[0]);
				ShaderBufferInfo[] buffers = props.buffers;
				for (int m = 0; m < buffers.Length; m++)
				{
					ShaderBufferInfo t2 = buffers[m];
					this.OnGUIShaderPropBuffer(t2);
				}
			}
			GUILayout.EndScrollView();
		}

		private void DrawStates(FrameDebuggerEventData curEventData)
		{
			FrameDebuggerBlendState blendState = curEventData.blendState;
			FrameDebuggerRasterState rasterState = curEventData.rasterState;
			FrameDebuggerDepthState depthState = curEventData.depthState;
			string text = string.Format("{0} {1}", blendState.srcBlend, blendState.dstBlend);
			if (blendState.srcBlendAlpha != blendState.srcBlend || blendState.dstBlendAlpha != blendState.dstBlend)
			{
				text += string.Format(", {0} {1}", blendState.srcBlendAlpha, blendState.dstBlendAlpha);
			}
			EditorGUILayout.LabelField("Blend", text, new GUILayoutOption[0]);
			if (blendState.blendOp != BlendOp.Add || blendState.blendOpAlpha != BlendOp.Add)
			{
				string label;
				if (blendState.blendOp == blendState.blendOpAlpha)
				{
					label = blendState.blendOp.ToString();
				}
				else
				{
					label = string.Format("{0}, {1}", blendState.blendOp, blendState.blendOpAlpha);
				}
				EditorGUILayout.LabelField("BlendOp", label, new GUILayoutOption[0]);
			}
			if (blendState.writeMask != 15u)
			{
				string text2 = "";
				if (blendState.writeMask == 0u)
				{
					text2 += '0';
				}
				else
				{
					if ((blendState.writeMask & 2u) != 0u)
					{
						text2 += 'R';
					}
					if ((blendState.writeMask & 4u) != 0u)
					{
						text2 += 'G';
					}
					if ((blendState.writeMask & 8u) != 0u)
					{
						text2 += 'B';
					}
					if ((blendState.writeMask & 1u) != 0u)
					{
						text2 += 'A';
					}
				}
				EditorGUILayout.LabelField("ColorMask", text2, new GUILayoutOption[0]);
			}
			EditorGUILayout.LabelField("ZTest", depthState.depthFunc.ToString(), new GUILayoutOption[0]);
			EditorGUILayout.LabelField("ZWrite", (depthState.depthWrite != 0) ? "On" : "Off", new GUILayoutOption[0]);
			EditorGUILayout.LabelField("Cull", rasterState.cullMode.ToString(), new GUILayoutOption[0]);
			if (rasterState.slopeScaledDepthBias != 0f || rasterState.depthBias != 0)
			{
				string label2 = string.Format("{0}, {1}", rasterState.slopeScaledDepthBias, rasterState.depthBias);
				EditorGUILayout.LabelField("Offset", label2, new GUILayoutOption[0]);
			}
		}

		internal void OnGUI()
		{
			FrameDebuggerEvent[] frameEvents = FrameDebuggerUtility.GetFrameEvents();
			if (this.m_TreeViewState == null)
			{
				this.m_TreeViewState = new TreeViewState();
			}
			if (this.m_Tree == null)
			{
				this.m_Tree = new FrameDebuggerTreeView(frameEvents, this.m_TreeViewState, this, default(Rect));
				this.m_FrameEventsHash = FrameDebuggerUtility.eventsHash;
				this.m_Tree.m_DataSource.SetExpandedWithChildren(this.m_Tree.m_DataSource.root, true);
			}
			if (FrameDebuggerUtility.eventsHash != this.m_FrameEventsHash)
			{
				this.m_Tree.m_DataSource.SetEvents(frameEvents);
				this.m_FrameEventsHash = FrameDebuggerUtility.eventsHash;
			}
			int limit = FrameDebuggerUtility.limit;
			bool flag = this.DrawToolbar(frameEvents);
			if (!FrameDebuggerUtility.IsLocalEnabled() && !FrameDebuggerUtility.IsRemoteEnabled() && this.m_AttachProfilerUI.IsEditor())
			{
				GUI.enabled = true;
				if (!FrameDebuggerUtility.locallySupported)
				{
					EditorGUILayout.HelpBox("Frame Debugger requires multi-threaded renderer. Usually Unity uses that; if it does not, try starting with -force-gfx-mt command line argument.", MessageType.Warning, true);
				}
				EditorGUILayout.HelpBox("Frame Debugger lets you step through draw calls and see how exactly frame is rendered. Click Enable!", MessageType.Info, true);
			}
			else
			{
				float fixedHeight = EditorStyles.toolbar.fixedHeight;
				Rect dragRect = new Rect(this.m_ListWidth, fixedHeight, 5f, base.position.height - fixedHeight);
				dragRect = EditorGUIUtility.HandleHorizontalSplitter(dragRect, base.position.width, 200f, 200f);
				this.m_ListWidth = dragRect.x;
				Rect rect = new Rect(0f, fixedHeight, this.m_ListWidth, base.position.height - fixedHeight);
				Rect rect2 = new Rect(this.m_ListWidth + 4f, fixedHeight + 4f, base.position.width - this.m_ListWidth - 8f, base.position.height - fixedHeight - 8f);
				this.DrawEventsTree(rect);
				EditorGUIUtility.DrawHorizontalSplitter(dragRect);
				this.DrawCurrentEvent(rect2, frameEvents);
			}
			if (flag || limit != FrameDebuggerUtility.limit)
			{
				this.RepaintOnLimitChange();
			}
			if (this.m_RepaintFrames > 0)
			{
				this.m_Tree.SelectFrameEventIndex(FrameDebuggerUtility.limit);
				this.RepaintAllNeededThings();
				this.m_RepaintFrames--;
			}
		}

		private void RepaintOnLimitChange()
		{
			this.m_RepaintFrames = 4;
			this.RepaintAllNeededThings();
		}

		private void RepaintAllNeededThings()
		{
			EditorApplication.SetSceneRepaintDirty();
			base.Repaint();
		}

		private void DrawEventsTree(Rect rect)
		{
			this.m_Tree.OnGUI(rect);
		}
	}
}
