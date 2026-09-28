using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sirenix.OdinInspector.Editor.Drawers
{
    [DrawerPriority(0.0, 0.0, 2002.0)]
    public class ConditionDropdownAttributeDrawer : OdinAttributeDrawer<ConditionDropdownAttribute>
    {
        private string error;
        private GUIContent label;
        private bool isList;
        private bool isListElement;
        private Func<IEnumerable<ValueDropdownItem>> getValues;
        private Func<IEnumerable<object>> getSelection;
        private IEnumerable<object> result;
        private bool enableMultiSelect;
        private Dictionary<object, string> nameLookup;
        private ValueResolver<object> rawGetter;
        private LocalPersistentContext<bool> isToggled;

        private ValueResolver<bool> visibleIfResolver;

        /// <summary>Initializes this instance.</summary>
        protected override void Initialize()
        {
          this.visibleIfResolver = ValueResolver.Get<bool>(this.Property, this.Attribute.ShowIf, true);

          this.rawGetter = ValueResolver.Get<object>(this.Property, this.Attribute.ValuesGetter);
          this.isToggled = this.GetPersistentValue<bool>("Toggled", SirenixEditorGUI.ExpandFoldoutByDefault);
          this.error = this.rawGetter.ErrorMessage;
          this.isList = this.Property.ChildResolver is ICollectionResolver;
          this.isListElement = this.Property.Parent != null && this.Property.Parent.ChildResolver is ICollectionResolver;
          this.getSelection = (Func<IEnumerable<object>>) (() => this.Property.ValueEntry.WeakValues.Cast<object>());
          this.getValues = (Func<IEnumerable<ValueDropdownItem>>) (() =>
          {
            object source = this.rawGetter.GetValue();
            return source != null ? (source as IEnumerable).Cast<object>().Where<object>((Func<object, bool>) (x => x != null)).Select<object, ValueDropdownItem>((Func<object, ValueDropdownItem>) (x =>
            {
              switch (x)
              {
                case ValueDropdownItem valueDropdownItem3:
                  return valueDropdownItem3;
                case IValueDropdownItem _:
                  IValueDropdownItem valueDropdownItem4 = x as IValueDropdownItem;
                  return new ValueDropdownItem(valueDropdownItem4.GetText(), valueDropdownItem4.GetValue());
                default:
                  return new ValueDropdownItem((string) null, x);
              }
            })) : (IEnumerable<ValueDropdownItem>) null;
          });
          this.ReloadDropdownCollections();
        }

        private void ReloadDropdownCollections()
        {
          if (this.error != null)
            return;
          object obj = (object) null;
          object source = this.rawGetter.GetValue();
          if (source != null)
            obj = (source as IEnumerable).Cast<object>().FirstOrDefault<object>();
          if (obj is IValueDropdownItem)
          {
            IEnumerable<ValueDropdownItem> valueDropdownItems = this.getValues();
            this.nameLookup = new Dictionary<object, string>((IEqualityComparer<object>) new IValueDropdownEqualityComparer(false));
            foreach (ValueDropdownItem key in valueDropdownItems)
              this.nameLookup[(object) key] = key.Text;
          }
          else
            this.nameLookup = (Dictionary<object, string>) null;
        }

        private static IEnumerable<ValueDropdownItem> ToValueDropdowns(IEnumerable<object> query)
        {
          return query.Select<object, ValueDropdownItem>((Func<object, ValueDropdownItem>) (x =>
          {
            switch (x)
            {
              case ValueDropdownItem valueDropdowns2:
                return valueDropdowns2;
              case IValueDropdownItem _:
                IValueDropdownItem valueDropdownItem = x as IValueDropdownItem;
                return new ValueDropdownItem(valueDropdownItem.GetText(), valueDropdownItem.GetValue());
              default:
                return new ValueDropdownItem((string) null, x);
            }
          }));
        }

        /// <summary>
        /// Draws the property with GUILayout support. This method is called by DrawPropertyImplementation if the GUICallType is set to GUILayout, which is the default.
        /// </summary>
        protected override void DrawPropertyLayout(GUIContent label)
        {
          if (this.visibleIfResolver.HasError)
          {
            SirenixEditorGUI.ErrorMessageBox(this.visibleIfResolver.ErrorMessage);
            return;
          }

          // 根据 ShowIf 是否显示下拉
          if (!this.visibleIfResolver.GetValue())
          {
            this.CallNextDrawer(label);
            return;
          }

          this.label = label;
          if (this.Property.ValueEntry == null)
            this.CallNextDrawer(label);
          else if (this.error != null)
          {
            SirenixEditorGUI.ErrorMessageBox(this.error);
            this.CallNextDrawer(label);
          }
          else if (this.isList)
          {
            if (this.Attribute.DisableListAddButtonBehaviour)
            {
              this.CallNextDrawer(label);
            }
            else
            {
              Action customAddFunction = CollectionDrawerStaticInfo.NextCustomAddFunction;
              CollectionDrawerStaticInfo.NextCustomAddFunction = new Action(this.OpenSelector);
              this.CallNextDrawer(label);
              if (this.result != null)
              {
                this.AddResult(this.result);
                this.result = (IEnumerable<object>) null;
              }
              CollectionDrawerStaticInfo.NextCustomAddFunction = customAddFunction;
            }
          }
          else if (this.Attribute.DrawDropdownForListElements || !this.isListElement)
            this.DrawDropdown();
          else
            this.CallNextDrawer(label);
        }

        private void AddResult(IEnumerable<object> query)
        {
          if (this.isList)
          {
            ICollectionResolver childResolver = this.Property.ChildResolver as ICollectionResolver;
            if (this.enableMultiSelect)
              childResolver.QueueClear();
            foreach (object obj in query)
            {
              object[] values = new object[this.Property.ParentValues.Count];
              for (int index = 0; index < values.Length; ++index)
                values[index] = !this.Attribute.CopyValues ? obj : Sirenix.Serialization.SerializationUtility.CreateCopy(obj);
              childResolver.QueueAdd(values);
            }
          }
          else
          {
            object obj = query.FirstOrDefault<object>();
            for (int index = 0; index < this.Property.ValueEntry.WeakValues.Count; ++index)
            {
              if (this.Attribute.CopyValues)
                this.Property.ValueEntry.WeakValues[index] = Sirenix.Serialization.SerializationUtility.CreateCopy(obj);
              else
                this.Property.ValueEntry.WeakValues[index] = obj;
            }
          }
        }

        private void DrawDropdown()
        {
          IEnumerable<object> objects;
          if (this.Attribute.AppendNextDrawer && !this.isList)
          {
            GUILayout.BeginHorizontal();
            float width = 15f;
            if (this.label != null)
              width += GUIHelper.BetterLabelWidth;
            GUIContent btnLabel = GUIHelper.TempContent("");
            if (this.Property.Info.TypeOfValue == typeof (System.Type))
              btnLabel.image = (Texture) GUIHelper.GetAssetThumbnail((UnityEngine.Object) null, this.Property.ValueEntry.WeakSmartValue as System.Type, false);
            objects = OdinSelector<object>.DrawSelectorDropdown(this.label, btnLabel, new Func<Rect, OdinSelector<object>>(this.ShowSelector), !this.Attribute.OnlyChangeValueOnConfirm, GUIStyle.none, (GUILayoutOption[]) GUILayoutOptions.Width(width));
            if (Event.current.type == UnityEngine.EventType.Repaint)
            {
              Rect position = GUILayoutUtility.GetLastRect().AlignRight(15f);
              position.y += 4f;
              SirenixGUIStyles.PaneOptions.Draw(position, GUIContent.none, 0);
            }
            GUILayout.BeginVertical();
            bool inAppendedDrawer = this.Attribute.DisableGUIInAppendedDrawer;
            if (inAppendedDrawer)
              GUIHelper.PushGUIEnabled(false);
            this.CallNextDrawer((GUIContent) null);
            if (inAppendedDrawer)
              GUIHelper.PopGUIEnabled();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
          }
          else
          {
            GUIContent btnLabel = GUIHelper.TempContent(this.GetCurrentValueName());
            if (this.Property.Info.TypeOfValue == typeof (System.Type))
              btnLabel.image = (Texture) GUIHelper.GetAssetThumbnail((UnityEngine.Object) null, this.Property.ValueEntry.WeakSmartValue as System.Type, false);
            if (!this.Attribute.HideChildProperties && this.Property.Children.Count > 0)
            {
              Rect valueRect;
              this.isToggled.Value = SirenixEditorGUI.Foldout(this.isToggled.Value, this.label, out valueRect);
              objects = OdinSelector<object>.DrawSelectorDropdown(valueRect, btnLabel, new Func<Rect, OdinSelector<object>>(this.ShowSelector), !this.Attribute.OnlyChangeValueOnConfirm);
              if (SirenixEditorGUI.BeginFadeGroup((object) this, this.isToggled.Value))
              {
                ++EditorGUI.indentLevel;
                for (int index = 0; index < this.Property.Children.Count; ++index)
                {
                  InspectorProperty child = this.Property.Children[index];
                  child.Draw(child.Label);
                }
                --EditorGUI.indentLevel;
              }
              SirenixEditorGUI.EndFadeGroup();
            }
            else
              objects = OdinSelector<object>.DrawSelectorDropdown(this.label, btnLabel, new Func<Rect, OdinSelector<object>>(this.ShowSelector), !this.Attribute.OnlyChangeValueOnConfirm, (GUIStyle) null);
          }
          if (objects == null || !objects.Any<object>())
            return;
          this.AddResult(objects);
        }

        private void OpenSelector()
        {
          this.ReloadDropdownCollections();
          this.ShowSelector(new Rect(Event.current.mousePosition, Vector2.zero)).SelectionConfirmed += (Action<IEnumerable<object>>) (x => this.result = x);
        }

        private OdinSelector<object> ShowSelector(Rect rect)
        {
          GenericSelector<object> selector = this.CreateSelector();
          rect.x = (float) (int) rect.x;
          rect.y = (float) (int) rect.y;
          rect.width = (float) (int) rect.width;
          rect.height = (float) (int) rect.height;
          if (this.Attribute.AppendNextDrawer && !this.isList)
            rect.xMax = GUIHelper.GetCurrentLayoutRect().xMax;
          selector.ShowInPopup(rect, new Vector2((float) this.Attribute.DropdownWidth, (float) this.Attribute.DropdownHeight));
          return (OdinSelector<object>) selector;
        }

        private GenericSelector<object> CreateSelector()
        {
          bool isUniqueList = this.Attribute.IsUniqueList;
          IEnumerable<ValueDropdownItem> source = this.getValues() ?? Enumerable.Empty<ValueDropdownItem>();
          if (source.Any<ValueDropdownItem>())
          {
            if (this.isList && this.Attribute.ExcludeExistingValuesInList || this.isListElement & isUniqueList)
            {
              List<ValueDropdownItem> list = source.ToList<ValueDropdownItem>();
              InspectorProperty parent = this.Property.FindParent((Func<InspectorProperty, bool>) (x => x.ChildResolver is ICollectionResolver), true);
              IValueDropdownEqualityComparer comparer = new IValueDropdownEqualityComparer(false);
              parent.ValueEntry.WeakValues.Cast<IEnumerable>().SelectMany<IEnumerable, object>((Func<IEnumerable, IEnumerable<object>>) (x => x.Cast<object>())).ForEach<object>((Action<object>) (x => list.RemoveAll((Predicate<ValueDropdownItem>) (c => comparer.Equals((object) c, x)))));
              source = (IEnumerable<ValueDropdownItem>) list;
            }
            if (this.nameLookup != null)
            {
              foreach (ValueDropdownItem valueDropdownItem in source)
              {
                if (valueDropdownItem.Value != null)
                  this.nameLookup[valueDropdownItem.Value] = valueDropdownItem.Text;
              }
            }
          }
          bool flag = this.Attribute.NumberOfItemsBeforeEnablingSearch == 0 || source != null && source.Take<ValueDropdownItem>(this.Attribute.NumberOfItemsBeforeEnablingSearch).Count<ValueDropdownItem>() == this.Attribute.NumberOfItemsBeforeEnablingSearch;
          GenericSelector<object> selector = new GenericSelector<object>(this.Attribute.DropdownTitle, false, source.Select<ValueDropdownItem, GenericSelectorItem<object>>((Func<ValueDropdownItem, GenericSelectorItem<object>>) (x => new GenericSelectorItem<object>(x.Text, x.Value))));
          this.enableMultiSelect = this.isList & isUniqueList && !this.Attribute.ExcludeExistingValuesInList;
          if (this.Attribute.FlattenTreeView)
            selector.FlattenedTree = true;
          if (((!this.isList ? 0 : (!this.Attribute.ExcludeExistingValuesInList ? 1 : 0)) & (isUniqueList ? 1 : 0)) != 0)
            selector.CheckboxToggle = true;
          else if (!this.Attribute.DoubleClickToConfirm && !this.enableMultiSelect)
            selector.EnableSingleClickToSelect();
          if (this.isList && this.enableMultiSelect)
          {
            selector.SelectionTree.Selection.SupportsMultiSelect = true;
            selector.DrawConfirmSelectionButton = true;
          }
          selector.SelectionTree.Config.DrawSearchToolbar = flag;
          IEnumerable<object> selection = Enumerable.Empty<object>();
          if (!this.isList)
            selection = this.getSelection();
          else if (this.enableMultiSelect)
            selection = this.getSelection().SelectMany<object, object>((Func<object, IEnumerable<object>>) (x => (x as IEnumerable).Cast<object>()));
          selector.SetSelection(selection);
          selector.SelectionTree.EnumerateTree().AddThumbnailIcons(true);
          if (this.Attribute.ExpandAllMenuItems)
            selector.SelectionTree.EnumerateTree((Action<OdinMenuItem>) (x => x.Toggled = true));
          if (this.Attribute.SortDropdownItems)
            selector.SelectionTree.SortMenuItemsByName();
          return selector;
        }

        private string GetCurrentValueName()
        {
          if (EditorGUI.showMixedValue)
            return "—";
          object weakSmartValue = this.Property.ValueEntry.WeakSmartValue;
          string name = (string) null;
          if (this.nameLookup != null && weakSmartValue != null)
            this.nameLookup.TryGetValue(weakSmartValue, out name);
          return new GenericSelectorItem<object>(name, weakSmartValue).GetNiceName();
        }
    }
}