using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using System.Xml.Serialization;
using UnityEngine;
using UnityInjector;
using UnityInjector.Attributes;

namespace CM3D2.EditMenuFilter.Plugin
{
	[PluginName( "EditMenuFilter" )]
	[PluginVersion( "1.2.0.0" )]

	// 設定データクラス XMLでシリアライズして保存する
	public class ConfigData
	{
		public int HistoryMax = 35;						// 履歴最大数
		public bool IsAnd = true;							// AND検索か OR検索か
		public bool IgnoreCase = false;					// 大文字小文字を無視して検索するかどうか
		public bool FilterDesc = false;					// 説明も検索するかどうか(プリセットの場合は名前の表示ボタン)
		public List<string> History = new List<string>();	// 現在の履歴リスト
	}

	public class EditMenuFilter : UnityInjector.PluginBase
	{
		private bool m_isSceneEdit = false;
		private bool m_isInstallMenuItem = false;
		private bool m_isInstallMenuSet = false;
		private bool m_isInstallPreset = false;
		public string MenuXmlPath { get; private set; }
		public string SetXmlPath { get; private set; }
		public string PresetXmlPath { get; private set; }

		public static EditMenuFilter Instance { get; private set; }
		private void Awake()
		{
			// セーブデータ名をセット
			MenuXmlPath   = DataPath + @"\" + Name + ".xml";
			SetXmlPath    = DataPath + @"\" + Name + "_Set.xml";
			PresetXmlPath = DataPath + @"\" + Name + "_Preset.xml";
		}
		private void OnLevelWasLoaded( int level )
		{
			Instance = this;
			m_isSceneEdit = false;
			m_isInstallMenuItem = false;
			m_isInstallMenuSet = false;
			m_isInstallPreset = false;

			// エディットならインストールフラグを立てる
			if ( Application.loadedLevelName == "SceneEdit" )
			{
				m_isSceneEdit = true;
				m_isInstallMenuItem = true;
				m_isInstallMenuSet = true;
				m_isInstallPreset = true;
			}
		}

		private void Update()
		{
			if ( m_isSceneEdit )
			{
				// インストール開始
				if ( m_isInstallMenuItem )
				{
					m_isInstallMenuItem = InstallMenu( "ScrollPanel-MenuItem", ItemFilterCtrl.Type.Menu );
				}
				if ( m_isInstallMenuSet )
				{
					m_isInstallMenuSet = InstallMenu( "ScrollPanel-SetItem", ItemFilterCtrl.Type.Set );
				}
				if ( m_isInstallPreset )
				{
					m_isInstallPreset = InstallMenu( "PresetPanel/PresetViewer", ItemFilterCtrl.Type.Preset );
				}
			}
		}
		// エディットメニューのScrollPanel-MenuItemにフィルター用オブジェクトをつける
		private bool InstallMenu( string panelName, ItemFilterCtrl.Type menuType )
		{
			Transform uiRoot = GameObject.Find( "UI Root" ).transform;

			if ( uiRoot == null ) { return true; }
			// プロフィールの名前を複製して入力を作る
			Transform menuItem = uiRoot.Find( panelName );
			Transform profName = uiRoot.Find( "ProfilePanel/CharacterInfo/Name" );

			if ( menuItem && profName &&
				 menuItem.Find( "ItemFilterPlugin" ) == null )
			{
				GameObject filter = NGUITools.AddChild( menuItem.gameObject, profName.gameObject );

				// ItemFilterCtrlコンポーネントをつける
				if ( filter &&
					 filter.GetComponent<ItemFilterCtrl>() == null )
				{
					filter.name = "ItemFilterPlugin";
					ItemFilterCtrl ctrl = filter.gameObject.AddComponent<ItemFilterCtrl>();
					ctrl.FilterType = menuType;
					// 作成終了
					return false;
				}
			}

			// 失敗したのでリトライ
			return true;
		}
	}


	//////////////////////////////////////////////////////
	//////////////////////////////////////////////////////
	//////////////////////////////////////////////////////
	/// アイテムのフィルターを制御するコンポーネント
	public class ItemFilterCtrl : MonoBehaviour
	{
		public enum Type { Menu, Set, Preset }

		public Type FilterType { get; set; }		// フィルターのタイプ

		private ConfigData m_config = null;			// 設定データ Config で参照する

		private bool m_bFilterd = false;			// フィルター中フラグ
		private bool m_bFilteOnChange = false;      // ほかのメニューに変えた際のフィルター実行フラグ
		private bool m_bAddPresetLabel = false;		// プリセットに名前のラベルを付ける初期化時のフラグ

		// UIのパーツ
		private SceneEdit m_sceneEdit = null;
		private UIGrid m_grid = null;				// 通常メニューとセット用
		private UITable m_table = null;				// プリセット用
		private Transform m_gridTableTrans = null;  // UIGrid か UITable の Transform
		private UIPanel m_scrollViewPanel = null;
		private UIScrollView m_scrollView = null;
		private UIScrollBar m_scrollBar = null;
		private UIInput m_filterInput = null;
		private UILabel m_filterLabel = null;
		private BoxCollider m_filterCollider = null;

		private UIButton m_setumeiBtn = null;
		private UILabel m_setumeiBtnLabel = null;
		private UISprite m_setumeiFrameSprite = null;

		private UIButton m_icBtn = null;
		private UILabel m_icBtnLabel = null;
		private UISprite m_icFrameSprite = null;

		private UIButton m_andOrBtn = null;
		private UILabel m_andOrBtnLabel = null;
		private UISprite m_andOrFrameSprite = null;

		private UIPopupList m_popupList = null;

		private bool m_isInstall = false;
		private bool m_isPopupStart = false;
		
		// 通常メニューとセットの場合とプリセットの場合で、表示/非表示時に実行するメソッドを変える
		private delegate void ShowFunc();
		private delegate void HideFunc( string str );
		private ShowFunc m_show = null;
		private HideFunc m_hide = null;

		PresetMgr.Filter m_currentFilter = PresetMgr.Filter.None;// 前回のUpdate()から PresetMgr.m_currentActiveFilterBtnName が変わったかの検知用
		int m_presetChildCount = -1;				// プリセットの数が増減した際の検知用

		// タイプ毎のセーブデータ名を取得する
		protected virtual string GetXmlPath()
		{
			switch( FilterType )
			{
				case Type.Menu:		return EditMenuFilter.Instance.MenuXmlPath;
				case Type.Set:		return EditMenuFilter.Instance.SetXmlPath;
				case Type.Preset:	return EditMenuFilter.Instance.PresetXmlPath;
			}
			return EditMenuFilter.Instance.MenuXmlPath;
		}

		// 設定データ
		public ConfigData Config
		{
			get
			{
				if ( m_config == null )
				{
					try
					{
						// XMLから読み込み
						StreamReader sr = new StreamReader( GetXmlPath(), new System.Text.UTF8Encoding( false ) );
						XmlSerializer serializer = new XmlSerializer( typeof( ConfigData ) );
						m_config = (ConfigData)serializer.Deserialize( sr );
						sr.Close();
					}
					catch
					{
						m_config = new ConfigData();
					}
				}

				return m_config;
			}
		}

		// 設定データのセーブ
		public void SaveData()
		{
			// XMLへ書き込み
			StreamWriter sw = new StreamWriter( GetXmlPath(), false, new System.Text.UTF8Encoding( false ) );
			XmlSerializer serializer = new XmlSerializer( typeof( ConfigData ) );
			serializer.Serialize( sw, Config );
			sw.Close();
		}

		private void Start()
		{
			m_bFilterd = false;
			m_bFilteOnChange = false;

			Transform uiRoot = GameObject.Find( "UI Root" ).GetTransform();
			UIPanel panel = gameObject.AddComponent<UIPanel>();  // プロフィールの名前にはパネルが付いていないので自分でつける
			m_sceneEdit = GameObject.Find( "__SceneEdit__" ).GetGetComponent<SceneEdit>();
			m_scrollBar = transform.parent.GetGetComponentInChildren<UIScrollBar>(false);
			m_scrollView = transform.parent.GetGetComponentInChildren<UIScrollView>(false);
			m_scrollViewPanel = m_scrollView.GetGetComponent<UIPanel>();

			if ( FilterType == Type.Preset )
			{
				m_show = ShowPreset;
				m_hide = HidePreset;
				m_table = m_scrollView.GetGetComponentInChildren<UITable>(false);
				if ( m_table ) { m_gridTableTrans = m_table.transform; }
				m_bAddPresetLabel = true;
				m_currentFilter = PresetMgr.m_currentActiveFilterBtnName;
			}
			else
			{
				m_show = ShowMenu;
				m_hide = HideMenu;
				m_grid = m_scrollView.GetGetComponentInChildren<UIGrid>(false);
				if ( m_grid ) { m_gridTableTrans = m_grid.transform; }
			}

			if ( uiRoot && panel && m_sceneEdit && m_gridTableTrans && m_scrollViewPanel && m_scrollView && m_scrollBar )
			{
				GameObject go;
				UISprite spr;
				Vector3 pos;

				panel.depth = m_scrollViewPanel.depth + 2;

				// 場所を設定
				transform.localPosition = new Vector3( -594, (FilterType == Type.Preset) ? 480 : 495, 0 );

				int baseDepth = m_scrollBar.GetComponent<UIPanel>().depth;

				// Title と FirstName は不要なので消す
				DestroyImmediate( transform.Find( "Title" ).GetGameObject() );
				DestroyImmediate( transform.Find( "FirstName" ).GetGameObject() );

				// Chu B Lip の場合はBGが無く、自身(this.gameObject)に UISprite が付いている
				// なので、その場合は自身の UISprite は消して他からBGをコピーしてくる
				spr = gameObject.GetComponent<UISprite>();
				if ( spr )
				{
					// 自身の UISprite は不要なので消す
					DestroyImmediate( spr );

					// 「プロフィール」の「性格」に付いている「BG」をコピーしてくる
					Transform personalBG = uiRoot.Find( "ProfilePanel/CharacterInfo/Personal/BG" );
					if ( personalBG )
					{
						GameObject bg = NGUITools.AddChild( gameObject, personalBG.gameObject );

						bg.name = "BG";
						bg.transform.localPosition = new Vector3( 281, 0, -1 );
					}
				}

				go = transform.Find( "BG" ).GetGameObject();
				if ( go )
				{
					spr = go.GetComponent<UISprite>();
					spr.depth = baseDepth + 1;
					spr.width = 520;
					spr.height = 36;
				}

				go = transform.Find( "LastName" ).GetGameObject();
				if ( go )
				{
					go.name = "Name";
					spr = go.GetComponent<UISprite>();
					spr.depth += baseDepth;
					spr.width = 338;
					pos = go.transform.localPosition;
					pos.x = 58;
					pos.y = 16.5f;
					go.transform.localPosition = pos;

					// Chu B Lip でBGを作った場合、何故か一度表示を切り替えないとdepthの順で表示されない…
					go.SetActive( false );
					go.SetActive( true );

					// Random は不要なので消す(COM用)
					DestroyImmediate( go.transform.Find( "Random" ).GetGameObject() );

					GameObject fi = go.transform.Find( "InputField" ).GetGameObject();
					if ( go && fi )
					{
						m_filterInput = go.GetComponent<UIInput>();
						m_filterCollider = go.GetComponent<BoxCollider>();
						m_filterLabel = fi.GetComponent<UILabel>();

						if ( m_filterInput && m_filterCollider && m_filterLabel )
						{
							m_filterInput.label = m_filterLabel;
							m_filterInput.onChange.Clear();
							m_filterInput.onSubmit.Clear();
							m_filterInput.defaultText = "";
							m_filterInput.value = "";
							m_filterInput.characterLimit = 50;
							m_filterInput.onChange.Add( new EventDelegate( OnFilterChange ) );
							m_filterInput.onSubmit.Add( new EventDelegate( OnFilterSubmit ) );
							m_filterInput.enabled = true;
							m_filterInput.selected = true;

							m_filterCollider.enabled = true;

							m_filterLabel.enabled = true;
							m_filterLabel.text = "";
							m_filterLabel.depth += baseDepth;
						}
					}
				}

				// And/Orボタンを作る
				_createButton( uiRoot, baseDepth, "ButtonAndOr", "And", 4.0f, new Vector3( 450, 0, 0 ),
								ref m_andOrBtn, ref m_andOrBtnLabel, ref m_andOrFrameSprite, AndOrClickCallback );

				// 「大文字小文字を無視」ON/OFFボタンを作る
				_createButton( uiRoot, baseDepth, "ButtonIC", "Aa", -1.0f, new Vector3( 485, 0, 0 ),
								ref m_icBtn, ref m_icBtnLabel, ref m_icFrameSprite, IcClickCallback );

				if ( FilterType == Type.Preset )
				{
					// 「名前」ON/OFFボタンを作る
					_createButton( uiRoot, baseDepth, "ButtonName", "名", 0.0f, new Vector3( 520, 0, 0 ),
									ref m_setumeiBtn, ref m_setumeiBtnLabel, ref m_setumeiFrameSprite, NameClickCallback );
				}
				else
				{
					// 「説明」ON/OFFボタンを作る
					_createButton( uiRoot, baseDepth, "ButtonDesc", "説", 0.0f, new Vector3( 520, 0, 0 ),
									ref m_setumeiBtn, ref m_setumeiBtnLabel, ref m_setumeiFrameSprite, SetumeiClickCallback );
				}
				// ポップアップを作る
				{
					Transform popupBase = uiRoot.Find( "ProfilePanel/CharacterInfo/Personal/PopupList" );

					if ( popupBase )
					{
						GameObject popup = NGUITools.AddChild( gameObject, popupBase.gameObject );

						popup.name = "PopupHistory";
						popup.transform.localPosition = new Vector3( 23, 0, 0 );

						BoxCollider popupCollider = popup.GetComponent<BoxCollider>();
						UISprite popupSprite = popup.GetComponent<UISprite>();
						UIButton popupButton = popup.GetComponent<UIButton>();
						UISprite symbolSprite = popup.transform.Find( "Symbol" ).GetGetComponent<UISprite>();
						m_popupList = popup.GetComponent<UIPopupList>();

						// Label は不要なので消す
						DestroyImmediate( popup.transform.Find( "Label" ).GetGameObject() );
						// LabelParent は不要なので消す(COM用)
						DestroyImmediate( popup.transform.Find( "LabelParent" ).GetGameObject() );

						if ( m_popupList && popupCollider && popupSprite && popupButton && symbolSprite )
						{
							// コピー元のリストと現在値をクリア
							m_popupList.Clear();
							m_popupList.value = null;
							// コピー元のコールバックをクリア
							m_popupList.onChange.Clear();
							m_popupList.onChange.Add( new EventDelegate( PopupCallback ) );

							m_popupList.fontSize = 20;
							m_popupList.enabled = true;
							popupCollider.enabled = true;

							// ポップアップのスプライトに合わせて▽もスケーリングされないようにアンカーをリセットする
							symbolSprite.SetAnchor( null, 0, 0, 0, 0 );
							symbolSprite.gameObject.SetActive( true );
							symbolSprite.MakePixelPerfect();
							symbolSprite.depth += baseDepth + 2;

							// ポップアップのスプライトのスケールを変更
							popupSprite.width = popupSprite.height = 33;
							// 深度を調整
							popupSprite.depth += baseDepth + 2;

							popupButton.state = UIButtonColor.State.Normal;
						}

					}
				}

				// ×ボタンを作る
				{
					if ( m_popupList )
					{
						GameObject batu = NGUITools.AddChild( gameObject, m_popupList.gameObject );

						if ( batu )
						{
							batu.name = "ButtonClear";
							batu.transform.localPosition = new Vector3( 398, 0, 0 );

							UIButton batuButton = batu.GetComponent<UIButton>();
							UISprite symbolSprite = batu.transform.Find( "Symbol" ).GetGetComponent<UISprite>();
							UISprite bgSprite = uiRoot.Find( "ProfilePanel/CharacterInfo/ProfileBase/BG" ).GetGetComponent<UISprite>();

							// ポップアップは不要なので削除
							Destroy( batu.GetComponent<UIPopupList>() );

							if ( batuButton && symbolSprite )
							{
								if ( bgSprite )
								{
									// ×の入っているアトラスをセット
									symbolSprite.atlas = bgSprite.atlas;
									symbolSprite.spriteName = "cm3d2_edit_profile_yotogiskill_sign_batu";
									symbolSprite.color = new Color( 0.5f, 0.5f, 0.5f, 1.0f );

									symbolSprite.transform.localPosition = new Vector3( 8, 0, 0 );
									symbolSprite.width = 18;
									symbolSprite.height = 18;
								}
								else
								{
									// Chu B Lip は×のスプライトが無さそうなので、ラベルで作る
									UILabel label = NGUITools.AddChild<UILabel>( batu );
									if ( label )
									{
										Font font = GameObject.Find( "SystemUI Root" ).GetComponentsInChildren<UILabel>()[0].trueTypeFont;
										label.trueTypeFont = font;
										label.fontSize = 20;
										label.width = 22;
										label.height = 22;
										label.pivot = UIWidget.Pivot.Center;
										label.overflowMethod = UILabel.Overflow.ClampContent;
										label.effectStyle = UILabel.Effect.None;
										label.text = "[000000]×";
										label.depth = symbolSprite.depth;
										label.transform.localPosition = new Vector3( 15, -1, 0 );
									}
									// ▼のスプライトは不要なので消す
									DestroyImmediate( symbolSprite.gameObject );
								}

								batuButton.onClick.Clear();
								batuButton.onClick.Add( new EventDelegate( ClearClickCallback ) );

								batuButton.defaultColor = new Color( 1.0f, 1.0f, 1.0f, 1.0f );
							}
							else
							{
								DestroyImmediate( batu );
							}
						}
					}
				}

				if ( m_filterInput && m_filterLabel &&
					 m_setumeiBtn && m_setumeiBtnLabel && m_setumeiFrameSprite &&
					 m_icBtn && m_icBtnLabel && m_icFrameSprite &&
					 m_andOrBtn && m_andOrBtnLabel && m_andOrFrameSprite &&
					 m_popupList )
				{
					// 非アクティブ項目が詰められる様にする
					if ( m_grid ) { m_grid.hideInactive = true; }
					m_isInstall = true;

					// ボタン状態を更新
					_updateButtonStr( Config.IsAnd, "And", -4.0f, "Or", 1.0f, m_andOrBtn, m_andOrBtnLabel, m_andOrFrameSprite );
					_updateButton( Config.IgnoreCase, m_icBtn, m_icBtnLabel, m_icFrameSprite );
					_updateButton( Config.FilterDesc, m_setumeiBtn, m_setumeiBtnLabel, m_setumeiFrameSprite );
					// 履歴ロード
					_loadPopup();
				}
				else
				{
					Console.WriteLine( "[EditMenuFilter]初期化に失敗しました。" );
					Destroy( gameObject );
				}
			}
		}

		// ボタンを作る
		private void _createButton( Transform uiRoot, int baseDepth, string name, string labelTxt, float spaceX, Vector3 pos,
			ref UIButton outBtn, ref UILabel label, ref UISprite sprite, EventDelegate.Callback callBack )
		{
			Transform btnBase = uiRoot.Find( "ScrollPanel-Category/Scroll View/UIGrid/ButtonCate(Clone)" );

			if ( btnBase )
			{
				GameObject btn = NGUITools.AddChild( gameObject, btnBase.gameObject );

				btn.name = name;
				btn.transform.localPosition = pos;

				label = btn.GetComponentsInChildren<UILabel>( true ).ElementAtOrDefault( 0 );
				ButtonEdit btnEdit = btn.GetComponentsInChildren<ButtonEdit>( true ).ElementAtOrDefault( 0 );
				outBtn = btnEdit.GetGetComponent<UIButton>();

				// ボタンの選択状態をセット
				if ( btnEdit && outBtn && label )
				{
					btnEdit.m_Category = null;
					btnEdit.m_goFrame = btn.transform.FindChild( "Frame" ).GetGameObject();
					if ( btnEdit.m_goFrame )
					{
						sprite = btnEdit.m_goFrame.transform.GetComponent<UISprite>();
						sprite.enabled = false;
						sprite.spriteName = "cm3d2_edit_profile_buttonselectcursor";
					}

					// クリック時のイベント設定
					outBtn.onClick.Clear();
					outBtn.onClick.Add( new EventDelegate( callBack ) );

					label.fontSize = 20;
					label.floatSpacingX = spaceX;
					label.text = labelTxt;
					label.depth += baseDepth;

					// サイズを調整
					UISprite[] sprAry = btn.GetComponentsInChildren<UISprite>( true );
					foreach ( var uiSpr in sprAry )
					{
						uiSpr.width = (int)(uiSpr.width * 0.25f);
						uiSpr.height = (int)(uiSpr.height * 0.6f);
						uiSpr.depth += baseDepth;
					}
				}
			}
		}
		private void Update()
		{
			if ( m_isInstall )
			{
				// プリセットの場合は本体のプリセットフィルターが変更された場合に
				// このプラグインのフィルターも再実行する
				if ( FilterType == Type.Preset )
				{
					if ( m_currentFilter != PresetMgr.m_currentActiveFilterBtnName )
					{
						m_bFilteOnChange = true;
						m_currentFilter = PresetMgr.m_currentActiveFilterBtnName;
					}

					// プリセットの数が増減した場合、項目が作り直されているので、再度ラベルを作り直す
					if ( m_gridTableTrans.childCount > 0 &&
						 m_presetChildCount != -1 &&
						 m_presetChildCount != m_gridTableTrans.childCount )
					{
						m_bAddPresetLabel = true;
						m_bFilteOnChange = true;    // 再実行も行う
						// m_presetChildCount は _AddPresetLabel() でラベルが作り終わった段階でセットされる
					}
				}

				// 子供が作られ終わってからプリセットに名前のラベルを付ける
				if ( m_bAddPresetLabel )
				{
					if ( m_gridTableTrans.childCount > 0 )
					{
						_AddPresetLabel();
						m_bAddPresetLabel = false;
					}
				}

				// 変更時のフィルター実行
				if ( m_bFilteOnChange )
				{
					// 子供が作られ終わったら
					if ( m_gridTableTrans.childCount > 0 )
					{
						// フィルター実行
						m_filterInput.Submit();
						// ちらつき防止の非表示を解除する
						m_scrollViewPanel.alpha = 1.0f;
						m_bFilteOnChange = false;
					}
				}
			}
		}

		private void OnEnable()
		{
			if ( m_isInstall )
			{
				// プリセット用の場合はラベルを作る
				if ( FilterType == Type.Preset )
				{
					m_bAddPresetLabel = true;
				}

				if ( m_bFilterd )
				{
					// ほかのメニューに変える際にもOnEnableに来るので
					// フィルター中なら変更後にフィルターする
					m_bFilteOnChange = true;

					// ちらつき防止の為にパネルを非表示にする
					m_scrollViewPanel.alpha = 0.0f;
				}
			}
		}

		// 文字入力時コールバック
		public void OnFilterChange()
		{
			if ( UIInput.current.value == "" )
			{
				m_show();
			}
		}

		// 入力決定時コールバック
		public void OnFilterSubmit()
		{
			if ( UIInput.current.value == "" )
			{
				// ShowMenu / ShowPreset
				m_show();
			}
			else
			{
				// HideMenu / HidePreset
				m_hide( UIInput.current.value );
				AddPopup( UIInput.current.value );
			}
		}

		// ボタンのメニューアイテムに対してアクションを実行する
		private void _menuItemAction( Action<GameObject, SceneEdit.SMenuItem> action )
		{
			if ( m_isInstall )
			{
				// ボタンに付いている ButtonEdit コンポーネントが持つ m_MenuItem を列挙
				// m_grid.GetChildList() は hideInactive が true だと非アクティブを返してくれないので自前
				var items =
				Enumerable
				.Range( 0, m_gridTableTrans.childCount )
				.Select( i => m_gridTableTrans.GetChild( i ) )
				.Where( item => item != null )
				.Select( item => item.Find( "Button" ) )
				.Where( btn => btn != null )
				.Select( btn => btn.GetComponent<ButtonEdit>() )
				.Where( edit => edit != null && edit.m_MenuItem != null )
				.Where( edit => edit.m_MenuItem.m_strMenuFileName != "" )   // フォルダを除外
				.Where( edit => edit.m_MenuItem.m_strMenuName != "無し" )	// アイテムを外すボタンを除外
				.Where( edit => !edit.m_MenuItem.m_strMenuName.Contains("脱ぐ・外す") )	// セットを外すボタンを除外
				.Select( edit => new { item = edit.transform.parent.gameObject, mi = edit.m_MenuItem } )
				.ToList();

				items.ForEach( s => action( s.item, s.mi ) );

				// エディットメニューの表示を更新する
				_resetView();
			}
		}

		// エディットメニューの表示を更新する
		private void _resetView()
		{
			if ( m_isInstall )
			{
				bool bEnabled = m_grid.enabled;

				m_grid.enabled = true;

				m_grid.Reposition();
				m_scrollView.ResetPosition();
				m_scrollBar.@value = 0.0f;

				// 一応、元に戻しておく
				m_grid.enabled = bEnabled;

				// 説明ポップアップが表示されている場合はアイテムを消すと消えてくれなくなるので消しておく
				m_sceneEdit.HoverOutCallback();
			}
		}

		public void HideMenu( string checkStr )
		{
			// スペースで分割
			string[] strAry = checkStr.Split( ' ' );

			_menuItemAction( ( item, mi ) =>
			{
				bool bContains = false;
				CompareInfo info = CultureInfo.CurrentCulture.CompareInfo;

				foreach ( var str in strAry )
				{
					// 大文字小文字を無視するか
					if ( Config.IgnoreCase )
					{
						// 大文字小文字/ひらがなカタカナ/全角半角を区別せずに比較する
						int result = info.IndexOf( mi.m_strMenuName, str,
											CompareOptions.IgnoreCase |
											CompareOptions.IgnoreWidth |
											CompareOptions.IgnoreKanaType );
						bContains = (result >= 0);
					}
					else
					{
						// 単純に同じ文字列を含んでいるかどうか
						bContains = mi.m_strMenuName.Contains( str );
					}

					// 説明もフィルターする場合
					if ( Config.FilterDesc )
					{
						// 既に名前に含まれていたなら調べる必要はない
						if ( !bContains )
						{
							if ( Config.IgnoreCase )
							{
								int result = info.IndexOf( mi.m_strInfo, str,
													CompareOptions.IgnoreCase |
													CompareOptions.IgnoreWidth |
													CompareOptions.IgnoreKanaType );
								bContains = (result >= 0);
							}
							else
							{
								bContains = mi.m_strInfo.Contains( str );
							}
						}
					}

					// ANDの場合、1つでも含まれていなければ非アクティブにする
					// ORの場合、1つでも含まれていればアクティブにする
					if ( Config.IsAnd )	{ if ( !bContains ) { break; } }
					else				{ if (  bContains ) { break; } }
				}

				// 含んでればアクティブ、含んでなければ非アクティブ
				item.SetActive( bContains );
			} );

			// フィルター中フラグをON
			m_bFilterd = true;
		}

		public void ShowMenu()
		{
			if ( m_bFilterd )
			{
				_menuItemAction( ( item, mi ) =>
				{
					if ( !item.activeSelf )
					{
						item.SetActive( true );
					}
				} );

				// フィルター中フラグをOFF
				m_bFilterd = false;
			}
		}

		/////////////////////////
		// プリセット用
		// プリセットリストのアイテムに対してアクションを実行する
		private void _AddPresetLabel()
		{
			if ( m_isInstall )
			{
				Font font = GameObject.Find( "SystemUI Root" ).GetComponentsInChildren<UILabel>()[0].trueTypeFont;

				_presetAction( null, true, ( item ) =>
				{
					UITexture tex = item.GetComponent<UITexture>();
					if ( tex )
					{
						UILabel label = NGUITools.AddChild<UILabel>( item );
						if ( label )
						{
							label.trueTypeFont = font;
							label.fontSize = 22;
							label.width = tex.width;
							label.height = tex.height;
							label.pivot = UIWidget.Pivot.TopLeft;
							label.overflowMethod = UILabel.Overflow.ResizeHeight;
							label.effectStyle = UILabel.Effect.Outline;
							label.text = "[00FF00]" + item.name.Replace( ".preset", "" );
							label.depth = tex.depth + 2;

							// 名前表示ON/OFFの状態をセット
							label.gameObject.SetActive( Config.FilterDesc );
						}
					}
				} );
				m_presetChildCount = m_gridTableTrans.childCount;
			}
		}

		public void HidePreset( string checkStr )
		{
			// スペースで分割
			string[] strAry = checkStr.Split( ' ' );

			_presetAction( ( item ) =>
			{
				bool bContains = false;
				CompareInfo info = CultureInfo.CurrentCulture.CompareInfo;
				
				foreach ( var str in strAry )
				{
					// 大文字小文字を無視するか
					if ( Config.IgnoreCase )
					{
						// 大文字小文字/ひらがなカタカナ/全角半角を区別せずに比較する
						int result = info.IndexOf( item.name, str,
											CompareOptions.IgnoreCase |
											CompareOptions.IgnoreWidth |
											CompareOptions.IgnoreKanaType );
						bContains = (result >= 0);
					}
					else
					{
						// 単純に同じ文字列を含んでいるかどうか
						bContains = item.name.Contains( str );
					}
					
					// ANDの場合、1つでも含まれていなければ非アクティブにする
					// ORの場合、1つでも含まれていればアクティブにする
					if ( Config.IsAnd )	{ if ( !bContains ) { break; } }
					else				{ if (  bContains ) { break; } }
				}

				// 含んでればアクティブ、含んでなければ非アクティブ
				item.SetActive( bContains );
			} );

			// フィルター中フラグをON
			m_bFilterd = true;
		}

		public void ShowPreset()
		{
			if ( m_bFilterd )
			{
				_presetAction( ( item ) =>
				{
					if ( !item.activeSelf )
					{
						item.SetActive( true );
					}
				} );

				// フィルター中フラグをOFF
				m_bFilterd = false;
			}
		}

		// プリセットリストのアイテムに対してアクションを実行する
		private void _presetAction( Action<GameObject> action )
		{
			_presetAction( _isCurrentActivePreset, true, action );
		}

		// プリセットリストのアイテムに対してアクションを実行する
		private void _presetAction( Func<Transform, bool> whereFunc, bool bResetView, Action<GameObject> action )
		{
			if ( m_isInstall )
			{
				// プリセットのメニューアイテムには ButtonEdit はない
				var items =
				Enumerable
				.Range( 0, m_gridTableTrans.childCount )
				.Select( i => m_gridTableTrans.GetChild( i ) )
				.Where( item => (whereFunc != null) ? (whereFunc( item )) : (true) )
				.Select( item => item.gameObject )
				.ToList();

				items.ForEach( s => action( s ) );

				if ( bResetView )
				{
					// エディットメニューの表示を更新する
					_resetPresetView();
				}
			}
		}

		// 現在のプリセットフィルターでアクティブなプリセットかどうかを判定する
		private bool _isCurrentActivePreset( Transform trans )
		{
			if ( trans == null ) { return false; }

			// フィルターが「全」の場合は全てアクティブ
			if ( PresetMgr.m_currentActiveFilterBtnName == PresetMgr.Filter.NotFilter ) { return true; }

			// それ以外は現在のプリセットフィルターでアクティブかどうかを右下のアイコンのスプライト名で判定する
			UISprite spr = trans.GetGetComponentInChildren<UISprite>(true);
			if ( spr )
			{
				switch( PresetMgr.m_currentActiveFilterBtnName )
				{
					case PresetMgr.Filter.All:  // 服/体 cm3d2_edit_priset_kindicon_clothes_body
						return spr.spriteName.EndsWith( "kindicon_clothes_body" );
					
					case PresetMgr.Filter.Wear: // 服    cm3d2_edit_priset_kindicon_clothes
						return spr.spriteName.EndsWith( "kindicon_clothes" );
					
					case PresetMgr.Filter.Body: // 体    cm3d2_edit_priset_kindicon_body
						return spr.spriteName.EndsWith( "kindicon_body" );
				}
			}
			return false;
		}

		// プリセットリストの表示を更新する
		private void _resetPresetView()
		{
			if ( m_isInstall )
			{
				m_table.Reposition();
				m_scrollView.ResetPosition();
				m_scrollBar.@value = 0.0f;
			}
		}

		// 名前ON/OFFボタンクリック時のコールバック
		public void NameClickCallback()
		{
			if ( m_isInstall )
			{
				Config.FilterDesc = !Config.FilterDesc;
				// ボタン状態更新
				_updateButton( Config.FilterDesc, m_setumeiBtn, m_setumeiBtnLabel, m_setumeiFrameSprite );

				SaveData();

				_presetAction( null, false, ( item ) =>
				{
					UILabel label = item.GetGetComponentInChildren<UILabel>(true);
					if ( label )
					{
						// 名前表示ON/OFFの状態をセット
						label.gameObject.SetActive( Config.FilterDesc );
					}
				} );
			}
		}
		/////////////////////////

		// 大文字小文字を無視するON/OFFボタンクリック時のコールバック
		public void IcClickCallback()
		{
			if ( m_isInstall )
			{
				Config.IgnoreCase = !Config.IgnoreCase;
				// ボタン状態更新
				_updateButton( Config.IgnoreCase, m_icBtn, m_icBtnLabel, m_icFrameSprite );

				SaveData();

				// フィルター中なら自動更新
				if ( m_bFilterd )
				{
					// フィルター実行
					m_filterInput.Submit();
				}
			}
		}

		// 説明ON/OFFボタンクリック時のコールバック
		public void SetumeiClickCallback()
		{
			if ( m_isInstall )
			{
				Config.FilterDesc = !Config.FilterDesc;
				// ボタン状態更新
				_updateButton( Config.FilterDesc, m_setumeiBtn, m_setumeiBtnLabel, m_setumeiFrameSprite );

				SaveData();

				// フィルター中なら自動更新
				if ( m_bFilterd )
				{
					// フィルター実行
					m_filterInput.Submit();
				}
			}
		}

		// And/Orボタンクリック時のコールバック
		public void AndOrClickCallback()
		{
			if ( m_isInstall )
			{
				Config.IsAnd = !Config.IsAnd;
				// ボタン状態更新
				_updateButtonStr( Config.IsAnd, "And", -4.0f, "Or", 1.0f, m_andOrBtn, m_andOrBtnLabel, m_andOrFrameSprite );

				SaveData();

				// フィルター中なら自動更新
				if ( m_bFilterd )
				{
					// フィルター実行
					m_filterInput.Submit();
				}
			}
		}

		// 現在の状態でボタンの色を変える
		private void _updateButton( bool bEnable, UIButton btn, UILabel label, UISprite sprite )
		{
			if ( m_isInstall )
			{
				// ON  ボタン 0.0f 0.5f 1.0f 1.0f フォント白
				// OFF ボタン 0.8f フォント 0.5f
				// 背景が白だとボタンが見えないので色を調整
				Color color;
				Color prsdCol;
				Color fontCol;
				if ( bEnable )
				{
					// 説明も検索時はボタンON
					color = new Color( 0.0f, 0.5f, 1.0f, 1.0f );    // 水色
					prsdCol = new Color( 0.8f, 0.8f, 0.8f, 1.0f );  // 濃い灰色
					fontCol = new Color( 1.0f, 1.0f, 1.0f, 1.0f );  // 白色
				}
				else
				{
					// ボタンOFF
					color = new Color( 0.8f, 0.8f, 0.8f, 1.0f );    // 濃い灰色
					prsdCol = new Color( 0.0f, 0.5f, 1.0f, 1.0f );  // 水色
					fontCol = new Color( 0.5f, 0.5f, 0.5f, 1.0f );  // 薄い灰色
				}
				btn.pressed = prsdCol;
				btn.hover = btn.defaultColor = color;
				label.color = fontCol;
				sprite.enabled = bEnable;
				btn.UpdateColor( false );
			}
		}

		// 現在の状態でボタンの文字を変える
		private void _updateButtonStr( bool bEnable, string enableText, float enableFloatX, string disableText, float disableFloatX, UIButton btn, UILabel label, UISprite sprite )
		{
			if ( m_isInstall )
			{
				// ON  ボタン 水色 フォント白
				// OFF ボタン 緑色 フォント白
				// 背景が白だとボタンが見えないので色を調整
				Color color;
				Color prsdCol;
				Color fontCol;
				float spaceX;
				if ( bEnable )
				{
					// 説明も検索時はボタンON
					color = new Color( 0.0f, 0.5f, 1.0f, 1.0f );      // 水色
					prsdCol = new Color( 0.15f, 0.55f, 0.10f, 1.0f ); // 緑色
					fontCol = new Color( 1.0f, 1.0f, 1.0f, 1.0f );    // 白色
					label.text = enableText;
					spaceX = enableFloatX;
				}
				else
				{
					// ボタンOFF
					color = new Color( 0.15f, 0.55f, 0.10f, 1.0f );   // 緑色
					prsdCol = new Color( 0.0f, 0.5f, 1.0f, 1.0f );    // 水色
					fontCol = new Color( 1.0f, 1.0f, 1.0f, 1.0f );    // 白色
					label.text = disableText;
					spaceX = disableFloatX;
				}
				btn.pressed = prsdCol;
				btn.hover = btn.defaultColor = color;
				label.color = fontCol;
				label.floatSpacingX = spaceX;
			sprite.enabled = false;
				btn.UpdateColor( false );
			}
		}


		// 履歴をロードしてポップアップにセット
		private void _loadPopup()
		{
			if ( m_isInstall )
			{
				// 一旦全部クリア
				m_popupList.Clear();

				// 設定データで履歴を初期化
				if ( Config.History.Count > 0 )
				{
					m_popupList.items = Config.History.GetRange( 0, Math.Min( Config.History.Count, Config.HistoryMax ) );
					m_popupList.itemData = Enumerable.Repeat( (object)null, m_popupList.items.Count ).ToList();

					// UIPopupList.Start() でコールバックが呼ばれてしまい最初の項目がセットされてしまう
					// 自分で選んだ時のみセットさせたいので初回は無視して、2回目以降は処理されるようにする
					m_isPopupStart = true;
				}
			}
		}

		// 履歴ポップアップ選択時のコールバック
		public void PopupCallback()
		{
			if ( m_isInstall )
			{
				if ( m_isPopupStart )
				{
					m_isPopupStart = false;
				}
				else
				{
					if ( !string.IsNullOrEmpty( m_popupList.value ) )
					{
						// 選んだ文字列をフィルター文字列に設定して
						m_filterInput.value = m_popupList.value;
						// フィルター実行
						m_filterInput.Submit();
					}
				}
			}
		}
		// 履歴ポップアップに文字列を追加
		public void AddPopup( string str )
		{
			if ( m_isInstall )
			{
				if ( string.IsNullOrEmpty( str ) ) { return; }

				int num = m_popupList.items.IndexOf( str );

				// 追加文字列が既に先頭にあるなら何もしない
				if ( num == 0 ) { return; }

				// 途中にあるなら削除する
				if ( num > 0 )
				{
					m_popupList.items.RemoveAt( num );
					m_popupList.itemData.RemoveAt( num );
				}

				// 履歴の最大数を超えていたら、一番最後の要素を削除する
				if ( m_popupList.items.Count >= Config.HistoryMax )
				{
					m_popupList.items.Remove( m_popupList.items.Last() );
					m_popupList.itemData.Remove( m_popupList.itemData.Last() );
				}

				// 先頭に追加する
				m_popupList.items.Insert( 0, str );
				m_popupList.itemData.Insert( 0, null );

				// 履歴を保存する
				Config.History = new List<string>( m_popupList.items );
				SaveData();
			}
		}

		// ×ボタンクリック時のコールバック
		public void ClearClickCallback()
		{
			if ( m_isInstall )
			{
				// フィルター文字列をクリアして
				m_filterInput.value = "";
				// フィルター実行
				m_filterInput.Submit();
				// 一応フォーカスも切っておく
				m_filterInput.RemoveFocus();
			}
		}
	}


	//////////////////////////////////////////////////////
	//////////////////////////////////////////////////////
	//////////////////////////////////////////////////////
	/// 拡張メソッド
	public static class Extensions
	{
		public static GameObject GetGameObject( this Transform o )
		{
			return (o != null) ? (o.gameObject) : null;
		}

		public static Transform GetTransform( this GameObject o )
		{
			return (o != null) ? (o.transform) : null;
		}

		// GetComponent
		public static T GetGetComponent<T>( this Transform o )
		{
			return (o != null) ? (o.GetComponent<T>()) : (default(T));
		}
		public static T GetGetComponent<T>( this GameObject o )
		{
			return (o != null) ? (o.GetComponent<T>()) : (default( T ));
		}
		public static T GetGetComponent<T>( this MonoBehaviour o )
		{
			return (o != null) ? (o.GetComponent<T>()) : (default( T ));
		}

		// GetComponentInChildren
		public static T GetGetComponentInChildren<T>( this Transform o, bool includeInactive )
		{
			return (o != null) ? (o.GetComponentInChildren<T>(includeInactive)) : (default( T ));
		}
		public static T GetGetComponentInChildren<T>( this GameObject o, bool includeInactive )
		{
			return (o != null) ? (o.GetComponentInChildren<T>(includeInactive)) : (default( T ));
		}
		public static T GetGetComponentInChildren<T>( this MonoBehaviour o, bool includeInactive )
		{
			return (o != null) ? (o.GetComponentInChildren<T>(includeInactive)) : (default( T ));
		}

		// Find -> GetGetComponent
		public static T FindAndGetComponent<T>( this Transform o, string name )
		{
			return o.Find( name ).GetGetComponent<T>();
		}
		public static T FindAndGetComponent<T>( this GameObject o, string name )
		{
			return (o != null) ? (o.transform.Find( name ).GetGetComponent<T>()) : (default(T));
		}
		public static T FindAndGetComponent<T>( this MonoBehaviour o, string name )
		{
			return (o != null) ? (o.transform.Find( name ).GetGetComponent<T>()) : (default( T ));
		}
		public static T FindAndGetComponent<T>( string name )
		{
			return GameObject.Find( name ).GetGetComponent<T>();
		}

		
	}
}
 