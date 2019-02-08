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
	[PluginFilter( "CM3D2x64" )]
	[PluginFilter( "CM3D2x86" )]
	[PluginFilter( "CM3D2OHx64" )]
	[PluginFilter( "CM3D2OHx86" )]
	[PluginFilter( "COM3D2x64" )]
	[PluginFilter( "COM3D2OHx64" )]
	[PluginFilter( "COM3D2_Trialx64" )]
	[PluginName( "EditMenuFilter" )]
	[PluginVersion( "1.0.0.0" )]

	// 設定データクラス XMLでシリアライズして保存する
	public class ConfigData
	{
		public int HistoryMax = 35;                         // 履歴最大数
		public bool IgnoreCase = false;                     // 大文字小文字を無視して検索するかどうか
		public bool FilterDesc = false;						// 説明も検索するかどうか
		public List<string> History = new List<string>();   // 現在の履歴リスト
	}

	public class EditMenuFilter : UnityInjector.PluginBase
	{
		private bool m_isSceneEdit = false;
		private bool m_isInstallMenu = false;
		public string XmlPath { get; private set; }

		public static EditMenuFilter Instance { get; private set; }
		private void Awake()
		{
			// セーブデータ名をセット
			XmlPath = DataPath + @"\" + Name + ".xml";
		}
		private void OnLevelWasLoaded( int level )
		{
			Instance = this;
			m_isSceneEdit = false;
			m_isInstallMenu = false;

			// エディットならインストールフラグを立てる
			if ( Application.loadedLevelName == "SceneEdit" )
			{
				m_isSceneEdit = true;
				m_isInstallMenu = true;
			}
		}

		private void Update()
		{
			if ( m_isSceneEdit )
			{
				// インストール開始
				if ( m_isInstallMenu )
				{
					InstallMenu();
				}
			}
		}
		// エディットメニューのScrollPanel-MenuItemにフィルター用オブジェクトをつける
		private void InstallMenu()
		{
			Transform uiRoot = GameObject.Find( "UI Root" ).transform;

			if ( uiRoot == null ) { return; }
			// プロフィールの名前を複製して入力を作る
			Transform menuItem = uiRoot.Find( "ScrollPanel-MenuItem" );
			Transform profName = uiRoot.Find( "ProfilePanel/CharacterInfo/Name" );

			if ( menuItem && profName &&
				 menuItem.Find( "ItemFilterPlugin" ) == null )
			{
				Transform filter = GameObject.Instantiate( profName );

				// ItemFilterCtrlコンポーネントをつける
				if ( filter &&
					 filter.GetComponent<ItemFilterCtrl>() == null )
				{
					// ScrollPanel-MenuItem の子供にする
					filter.SetParent( menuItem, false );

					filter.name = "ItemFilterPlugin";
					ItemFilterCtrl ctrl = filter.gameObject.AddComponent<ItemFilterCtrl>();
					m_isInstallMenu = false;
				}
			}
		}
	}

	//////////////////////////////////////////////////////
	//////////////////////////////////////////////////////
	//////////////////////////////////////////////////////
	/// アイテムのフィルターを制御するコンポーネント
	public class ItemFilterCtrl : MonoBehaviour
	{
		private ConfigData m_config = null;         // 設定データ Config で参照する

		private bool m_bFilterd = false;            // フィルター中フラグ
		private bool m_bFilteOnChange = false;      // ほかのメニューに変えた際のフィルター実行フラグ

		// UIのパーツ
		private SceneEdit m_sceneEdit = null;
		private UIGrid m_grid = null;
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

		private UIPopupList m_popupList = null;

		private bool m_isInstall = false;
		private bool m_isPopupStart = false;


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
						StreamReader sr = new StreamReader( EditMenuFilter.Instance.XmlPath, new System.Text.UTF8Encoding( false ) );
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
			StreamWriter sw = new StreamWriter( EditMenuFilter.Instance.XmlPath, false, new System.Text.UTF8Encoding( false ) );
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
			m_grid = m_scrollView.GetGetComponentInChildren<UIGrid>(false);

			if ( uiRoot && panel && m_sceneEdit && m_grid && m_scrollViewPanel && m_scrollView && m_scrollBar )
			{
				GameObject go;
				UISprite spr;
				Vector3 pos;

				panel.depth = m_scrollViewPanel.depth + 2;

				// 場所を設定
				transform.localPosition = new Vector3( -594, 495, 0 );

				int baseDepth = m_scrollBar.GetComponent<UIPanel>().depth;

				// Title と FirstName は不要なので消す
				DestroyImmediate( transform.Find( "Title" ).GetGameObject() );
				DestroyImmediate( transform.Find( "FirstName" ).GetGameObject() );

				go = transform.Find( "BG" ).GetGameObject();
				if ( go )
				{
					spr = go.GetComponent<UISprite>();
					spr.depth += baseDepth;
					spr.width = 520;
				}

				go = transform.Find( "LastName" ).GetGameObject();
				if ( go )
				{
					go.name = "Name";
					spr = go.GetComponent<UISprite>();
					spr.depth += baseDepth;
					spr.width = 373;
					pos = go.transform.localPosition;
					pos.x = 58;
					go.transform.localPosition = pos;

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

				// 「大文字小文字を無視」ON/OFFボタンを作る
				_createButton( uiRoot, baseDepth, "ButtonIC", "A        a", new Vector3( 485, 0, 0 ),
								ref m_icBtn, ref m_icBtnLabel, ref m_icFrameSprite, IcClickCallback );

				// 「説明」ON/OFFボタンを作る
				_createButton( uiRoot, baseDepth, "ButtonDesc", "説", new Vector3( 520, 0, 0 ),
								ref m_setumeiBtn, ref m_setumeiBtnLabel, ref m_setumeiFrameSprite, SetumeiClickCallback );
				
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
							batu.transform.localPosition = new Vector3( 432, 0, 0 );

							UIButton batuButton = batu.GetComponent<UIButton>();
							UISprite symbolSprite = batu.transform.Find( "Symbol" ).GetGetComponent<UISprite>();
							UISprite bgSprite = uiRoot.Find( "ProfilePanel/CharacterInfo/ProfileBase/BG" ).GetGetComponent<UISprite>();

							// ポップアップは不要なので削除
							Destroy( batu.GetComponent<UIPopupList>() );

							if ( batuButton && symbolSprite && bgSprite )
							{
								// ×の入っているアトラスをセット
								symbolSprite.atlas = bgSprite.atlas;
								symbolSprite.spriteName = "cm3d2_edit_profile_yotogiskill_sign_batu";
								symbolSprite.color = new Color( 0.5f, 0.5f, 0.5f, 1.0f );

								symbolSprite.transform.localPosition = new Vector3( 8, 0, 0 );
								symbolSprite.width = 18;
								symbolSprite.height = 18;

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
					 m_popupList )
				{
					// 非アクティブ項目が詰められる様にする
					m_grid.hideInactive = true;
					m_isInstall = true;

					// ボタン状態を更新
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
		private void _createButton( Transform uiRoot, int baseDepth, string name, string labelTxt, Vector3 pos,
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
				// 変更時のフィルター実行
				if ( m_bFilteOnChange )
				{
					// 子供が作られ終わったら
					if ( m_grid.transform.childCount > 0 )
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
				Show();
			}
		}

		// 入力決定時コールバック
		public void OnFilterSubmit()
		{
			if ( UIInput.current.value == "" )
			{
				Show();
			}
			else
			{
				Hide( UIInput.current.value );
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
				.Range( 0, m_grid.transform.childCount )
				.Select( i => m_grid.transform.GetChild( i ) )
				.Where( s => s != null )
				.Select( item => item.Find( "Button" ) )
				.Where( btn => btn != null )
				.Select( btn => btn.GetComponent<ButtonEdit>() )
				.Where( edit => edit != null && edit.m_MenuItem != null )
				.Where( edit => edit.m_MenuItem.m_strMenuFileName != "" )   // フォルダを除外
				.Where( edit => edit.m_MenuItem.m_strMenuName != "無し" ) // アイテムを外すボタンを除外
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

		public void Hide( string str )
		{
			_menuItemAction( ( item, mi ) =>
			{
				bool bContains = false;
				CompareInfo info = CultureInfo.CurrentCulture.CompareInfo;

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

				// 含んでればアクティブ、含んでなければ非アクティブ
				item.SetActive( bContains );
			} );

			// フィルター中フラグをON
			m_bFilterd = true;
		}

		public void Show()
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