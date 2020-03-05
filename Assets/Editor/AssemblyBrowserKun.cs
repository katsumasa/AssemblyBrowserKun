namespace UTJ
{
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using UnityEditor.UIElements;
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    // BindingFlagに関する参考
    // https://docs.microsoft.com/ja-jp/dotnet/api/system.reflection.bindingflags?view=netframework-4.8


    public class AssemblyBrowserKun : EditorWindow
    {

        // ListViewのItemとなる基底クラス
        public class ListViewItem
        {
            public enum ListViewItemType
            {
                Assembly,   // AssemblyListView用
                Class,      // ClassListView用
                Member,     // MemberListView用
            }

            static int counter = 0;
            public ListViewItemType listViewItemType;
            public string text;
            public int id;
            public bool isSelect;


            protected ListViewItem()
            {
                id = counter++;
                isSelect = false;
            }
        }


        // MemberListView用のItem
        public class MemberListViewItem : ListViewItem
        {
            public ClassListViewItem classData;
            public MemberInfo memberInfo;


            public MemberListViewItem(MemberInfo memberInfo, ClassListViewItem classData) : base()
            {
                this.memberInfo = memberInfo;
                this.classData = classData;
                text = memberInfo.Name;
                listViewItemType = ListViewItemType.Member;
            }


            ~MemberListViewItem()
            {
                memberInfo = null;
                classData = null;
                text = "";
            }


            // Memberがpublicであるか否か
            public bool IsPublic()
            {
                switch (memberInfo.MemberType)
                {
                    case MemberTypes.Field:
                        {
                            var fi = memberInfo as FieldInfo;
                            return fi.IsPublic;
                        }
                    case MemberTypes.Constructor:
                    case MemberTypes.Method:
                        {
                            var info = memberInfo as MethodBase;
                            return info.IsPublic;
                        }
                    case MemberTypes.Property:
                        {
                            // TODO
                            return true;
                        }
                }
                return true;
            }
        }


        // ClassListView用Item
        public class ClassListViewItem : ListViewItem
        {
            public AssemblyListViewItem assemblyData;
            public Type type;
            public List<MemberListViewItem> memberListViewItem;


            public ClassListViewItem(Type type, AssemblyListViewItem assemblyData) : base()
            {
                this.assemblyData = assemblyData;
                this.type = type;
                text = type.FullName;
                listViewItemType = ListViewItemType.Class;

                memberListViewItem = new List<MemberListViewItem>();

                // コンストラクター
                var constructors = type.GetConstructors(
                   BindingFlags.Public 
                   | BindingFlags.Static 
                   | BindingFlags.NonPublic 
                   | BindingFlags.Instance
                   );

                for (var i = 0; i < constructors.Length; i++)
                {
                    memberListViewItem.Add(new MemberListViewItem(constructors[i], this));
                }

                // メンバー（継承元も含める)
                var memberInfos = type.GetMembers(BindingFlags.Public 
                    | BindingFlags.Static 
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.FlattenHierarchy
                    );

                for (var i = 0; i < memberInfos.Length; i++)
                {
                    memberListViewItem.Add(new MemberListViewItem(memberInfos[i], this));
                }
            }


            ~ClassListViewItem()
            {
                if (memberListViewItem != null)
                {
                    memberListViewItem.Clear();
                    memberListViewItem = null;
                }
            }
        }


        // AssemblyListView用のItem
        public class AssemblyListViewItem : ListViewItem
        {
            public AssemblyName assemblyName;
            public List<ClassListViewItem> classListViewItem;


            // コンストラクタ
            public AssemblyListViewItem(AssemblyName assemblyName) : base()
            {
                listViewItemType = ListViewItemType.Assembly;

                this.assemblyName = assemblyName;
                var assembly = Assembly.Load(assemblyName);
                var types = assembly.GetTypes();
                classListViewItem = new List<ClassListViewItem>();
                for (var i = 0; i < types.Length; i++)
                {
                    classListViewItem.Add(new ClassListViewItem(types[i], this));
                }
            }


            ~AssemblyListViewItem()
            {
                if (classListViewItem != null)
                {
                    classListViewItem.Clear();
                    classListViewItem = null;
                }
            }
        }


        enum ClassVisibility
        {
            AllMember,
            PublicOnly
        }


        List<AssemblyListViewItem> assemblyListViewItems;
        List<ListViewItem> classListViewItems;
        ListView classListView;
        Label infoLabel;
        ClassVisibility classVisibility = ClassVisibility.AllMember;
        string keyword = "";


        [MenuItem("Window/AssemblyBrowserKun")]
        public static void ShowExample()
        {
            AssemblyBrowserKun wnd = GetWindow<AssemblyBrowserKun>();
            wnd.titleContent = new GUIContent("AssemblyBrowserKun");
        }


        public void OnEnable()
        {
            // Each editor window contains a root VisualElement object
            VisualElement root = rootVisualElement;


            // Import UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Editor/AssemblyBrowserKun.uxml");
            VisualElement labelFromUXML = visualTree.CloneTree();
            root.Add(labelFromUXML);

            // A stylesheet can be added to a VisualElement.
            // The style will be applied to the VisualElement and all of its children.
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/AssemblyBrowserKun.uss");


            infoLabel = root.Query<Label>("InfoLabel").AtIndex(0);

            classListViewItems = new List<ListViewItem>();
            assemblyListViewItems = new List<AssemblyListViewItem>();

            var assembly = Assembly.GetExecutingAssembly();
            //var assembly = Assembly.GetCallingAssembly();
            var assemblyNames = assembly.GetReferencedAssemblies();
            assemblyListViewItems = new List<AssemblyListViewItem>();
            for (var i = 0; i < assemblyNames.Length; i++)
            {
                var assemblyData = new AssemblyListViewItem(assemblyNames[i]);
                assemblyData.text = assemblyNames[i].Name;
                assemblyListViewItems.Add(assemblyData);
            }

            var toolBarSearchField = root.Query<ToolbarSearchField>("ToolBarSearchField").AtIndex(0);
            toolBarSearchField.RegisterValueChangedCallback(OnSearchTextChanged);

            var visibilityEnumField = root.Query<EnumField>("VisibilityEnumField").AtIndex(0);
            visibilityEnumField.Init(ClassVisibility.AllMember);
            visibilityEnumField.RegisterValueChangedCallback(OnVisibilityChangeCB);


            // The "makeItem" function will be called as needed
            // when the ListView needs more items to render
            Func<VisualElement> makeItem = () => new Label();

            // As the user scrolls through the list, the ListView object
            // will recycle elements created by the "makeItem"
            // and invoke the "bindItem" callback to associate
            // the element with the matching data item (specified as an index in the list)
            Action<VisualElement, int> bindItem = (e, i) => (e as Label).text = assemblyListViewItems[i].text;

            // Provide the list view with an explict height for every row
            // so it can calculate how many items to actually display
            const int itemHeight = 12;

            var listView = root.Query<ListView>("AssemblyListView").AtIndex(0);
            listView.itemsSource = assemblyListViewItems;
            listView.itemHeight = itemHeight;
            listView.makeItem = makeItem;
            listView.bindItem = bindItem;
            listView.selectionType = SelectionType.Multiple;            
            listView.onSelectionChanged += OnAssemblySelectionChanged;
            listView.Refresh();

            ClassListViewSourceReflesh();
            ClassListViewMake();
        }


        // EnumField用CB
        void OnVisibilityChangeCB(ChangeEvent<Enum> evt)
        {
            var val = evt.newValue;
            classVisibility = (ClassVisibility)val;
            ClassListViewSourceReflesh();
            classListView.Refresh();
        }


        // ToolbarSearchField用CB
        void OnSearchTextChanged(ChangeEvent<string> evt)
        {
            keyword = evt.newValue;
            ClassListViewSourceReflesh();
            classListView.Refresh();
        }


        // AssemblyListView用CB
        void OnAssemblySelectionChanged(List<System.Object> objects)
        {
            foreach (var assemblyData in assemblyListViewItems)
            {
                assemblyData.isSelect = false;
            }
            foreach (var obj in objects)
            {
                var assemblyData = obj as AssemblyListViewItem;
                assemblyData.isSelect = true;
            }
            SetAssemblyInfo(objects[0] as AssemblyListViewItem, infoLabel);
            ClassListViewSourceReflesh();
            classListView.Refresh();
        }


        // ClassListViewのソースを更新する
        void ClassListViewSourceReflesh()
        {
            classListViewItems.Clear();
            foreach (var assemblyData in assemblyListViewItems)
            {
                if (assemblyData.isSelect == false)
                {
                    continue;
                }
                foreach (var classData in assemblyData.classListViewItem)
                {
                    // Keywordチェック
                    if ((keyword != null) && (classData.text.Contains(keyword) == false))
                    {
                        continue;
                    }
                    classListViewItems.Add(classData);
                    if (classData.isSelect == false)
                    {
                        continue;
                    }
                    foreach (var memberData in classData.memberListViewItem)
                    {
                        // Visibilityチェック
                        if (classVisibility == ClassVisibility.PublicOnly && memberData.IsPublic() == false)
                        {
                            continue;
                        }
                        classListViewItems.Add(memberData);
                    }
                }
            }
        }


        // ClassListViewの生成
        void ClassListViewMake()
        {
            VisualElement root = rootVisualElement;
            classListView = root.Query<ListView>("ClassListView").AtIndex(0);
            classListView.itemsSource = classListViewItems;
            classListView.itemHeight = 16;
            classListView.makeItem = ClassListViewMakeItem;
            classListView.bindItem = ClassListViewBindItem;
            classListView.selectionType = SelectionType.Single;
            classListView.onSelectionChanged += OnSelectionChangeClassViewList;
            classListView.onItemChosen += OnSelectionClassListView;
            classListView.Refresh();
        }


        // ClassListViewのVisualElementはLabel
        private VisualElement ClassListViewMakeItem()
        {
            return new Label();
        }


        // ClassListViewのItemとVisualElementを結びつける処理
        private void ClassListViewBindItem(VisualElement element, int index)
        {
            var label = element as Label;

            var classListViewItem = classListViewItems[index];
            if (classListViewItem.listViewItemType == ListViewItem.ListViewItemType.Class)
            {
                if (classListViewItem.isSelect == true)
                {
                    label.text = "v " + classListViewItem.text;
                }
                else
                {
                    label.text = "> " + classListViewItem.text;
                }
            }
            else
            {
                label.text = "    " + classListViewItem.text;
            }
        }


        // ClassListViewでアイテムがダブルクリックされた時のCB
        void OnSelectionClassListView(System.Object obj)
        {
            var listViewItem = obj as ListViewItem;
            listViewItem.isSelect = listViewItem.isSelect ? false : true;
            ClassListViewSourceReflesh();
            classListView.Refresh();
        }


        // ClassListViewでアイテムが選択された時の処理
        private void OnSelectionChangeClassViewList(List<object> objects)
        {
            var listViewItem = objects[0] as ListViewItem;
            switch (listViewItem.listViewItemType)
            {
                case ListViewItem.ListViewItemType.Class:
                    {
                        SetClassInfo((ClassListViewItem)listViewItem, infoLabel);
                        break;
                    }
                case ListViewItem.ListViewItemType.Member:

                    {
                        var md = (MemberListViewItem)listViewItem;
                        switch (md.memberInfo.MemberType)
                        {
                            case MemberTypes.Method:
                                SetMethodInfo(md, infoLabel);
                                break;
                            case MemberTypes.Constructor:
                                SetConstructorInfo(md, infoLabel);
                                break;

                            case MemberTypes.Property:
                                SetPropertyInfo(md, infoLabel);
                                break;
                            case MemberTypes.Field:
                                SetFieldInfo(md, infoLabel);
                                break;
                        }
                    }
                    break;
            }
        }


        // Assemblyのインフォメーション
        private void SetAssemblyInfo(AssemblyListViewItem assemblyData, Label label)
        {
            var an = assemblyData.assemblyName;
            label.text = "";
            label.text += "CodeBase             : " + an.CodeBase + "\n";
            label.text += "CotentType           : " + an.ContentType.ToString() + "\n";
            label.text += "CultureInfo          : " + an.CultureInfo.ToString() + "\n";
            label.text += "CultureName          : " + an.CultureName + "\n";
            label.text += "EscapedCodeBase      : " + an.EscapedCodeBase + "\n";
            label.text += "Flags                : " + an.Flags.ToString() + "\n";
            label.text += "FullName             : " + an.FullName + "\n";
            label.text += "HashAlgorithm        : " + an.HashAlgorithm.ToString() + "\n";
            label.text += "Name                 : " + an.Name + "\n";
            label.text += "ProcessorArchitecture: " + an.ProcessorArchitecture.ToString() + "\n";
            label.text += "Version: " + an.Version.ToString() + "\n";
            label.text += "VersionCompatibility : " + an.VersionCompatibility.ToString() + "\n";
            label.text += "PublicKey token      :" + BitConverter.ToString(an.GetPublicKeyToken()) + "\n";
        }


        // Classのインフォメーション
        private void SetClassInfo(ClassListViewItem classData, Label label)
        {
            var ty = classData.type;


            label.text = "";
            label.text += ty.Name + " ";
            if (ty.IsValueType)
            {
                label.text += "struct ";
            }
            else
            {
                label.text += "class ";
            }
            label.text += "\n";

            TypeAttributes attr = ty.Attributes;
            TypeAttributes visibility = attr & TypeAttributes.VisibilityMask;
            switch (visibility)
            {
                case TypeAttributes.NotPublic:
                    label.text += "private ";
                    break;
                case TypeAttributes.Public:
                    label.text += "public ";
                    break;
                case TypeAttributes.NestedPublic:
                    label.text += "public ";
                    break;
                case TypeAttributes.NestedPrivate:
                    label.text += "private ";
                    break;
                case TypeAttributes.NestedFamANDAssem:

                    break;
                case TypeAttributes.NestedAssembly:
                    label.text += "internal ";
                    break;
                case TypeAttributes.NestedFamily:
                    label.text += "protected ";
                    break;
                case TypeAttributes.NestedFamORAssem:
                    label.text += "protected internal ";
                    break;
            }

            if ((attr & TypeAttributes.Abstract) != 0)
            {
                label.text += "abstruct ";
            }
            if ((attr & TypeAttributes.Sealed) != 0)
            {
                label.text += "sealed ";
            }

            TypeAttributes classSemantics = attr & TypeAttributes.ClassSemanticsMask;
            switch (classSemantics)
            {
                case TypeAttributes.Class:
                    if (ty.IsValueType)
                    {
                        label.text += "struct ";
                    }
                    else
                    {
                        label.text += "class ";
                    }
                    break;
                case TypeAttributes.Interface:
                    label.text += "interface class";
                    break;
            }
            var str = ty.UnderlyingSystemType.ToString();
            label.text += str.Remove(0, str.IndexOf(ty.Name));
            if (ty.BaseType != null)
            {
                label.text += " : " + ty.BaseType.Name + " ";
            }

            label.text += "\n";
            label.text += "\n";
            label.text += "Name                       : " + ty.Name + "\n";
            label.text += "NameSpace                  : " + ty.Namespace + "\n";
            label.text += "FullName                   : " + ty.FullName + "\n";
            label.text += "Module.Name                : " + ty.Module.Name + "\n";
            label.text += "Module.FulllyQualifiedName : " + ty.Module.FullyQualifiedName + "\n";
            label.text += "\n";
            label.text += "\n";
            label.text += "Reflection Sample\n";
            if (ty.IsPublic)
            {
                label.text += "var t = Typeof(" + ty.FullName + ");";
            }
            else
            {
                label.text += "var t = System.Reflection.Assembly.Load(\"" + ty.Module.Name + "\").GetType(\"" + ty.FullName + "\");";
            }
        }


        // コンストラクターのインフォメーション
        private void SetConstructorInfo(MemberListViewItem memberData, Label label)
        {
            var info = memberData.memberInfo as ConstructorInfo;
            MethodAttributes attr = info.Attributes;


            label.text = "";
            label.text += info.Name +" constructor\n";
            if (info.IsPrivate)
            {
                label.text += "private ";
            }
            else if (info.IsFamilyAndAssembly)
            {
                label.text += "private protected";
            }
            else if (info.IsFamilyOrAssembly)
            {
                label.text += "protected internal";
            }
            else if (info.IsPublic)
            {
                label.text += "public ";
            }
            else if (info.IsVirtual)
            {
                label.text += "virtual ";
            }
            else if (info.IsFamily)
            {
                label.text += " protected ";
            }

            if (info.IsStatic)
            {
                label.text += "static ";
            }

            label.text += info.Name + "(";
            var parameterInfos = info.GetParameters();
            for (var i = 0; i < parameterInfos.Length; i++)
            {
                var parameterInfo = parameterInfos[i];
                label.text += parameterInfo.ParameterType.ToString() + " ";
                label.text += parameterInfo.Name;
                if (i < parameterInfos.Length - 1)
                {
                    label.text += ",";
                }
            }
            label.text += ")\n";
        }


        // Methodのインフォメーション
        private void SetMethodInfo(MemberListViewItem memberData, Label label)
        {
            var mi = memberData.memberInfo as MethodInfo;

            label.text = "";
            label.text += mi.Name;
            label.text += " Method\n";
            label.text += "\n";

            if (mi.IsPrivate)
            {
                label.text += "private ";
            }
            else if (mi.IsFamilyAndAssembly)
            {
                label.text += "private protected";
            }
            else if (mi.IsFamilyOrAssembly)
            {
                label.text += "protected internal";
            }
            else if (mi.IsPublic)
            {
                label.text += "public ";
            }
            else if (mi.IsVirtual)
            {
                label.text += "virtual ";
            }
            else if (mi.IsFamily)
            {
                label.text += " protected ";
            }

            if (mi.IsStatic)
            {
                label.text += "static ";
            }

            if (mi.IsAbstract)
            {
                label.text += "abstract ";
            }
            else if (mi.IsFinal)
            {
                label.text += "final ";
            }
            else if (mi.IsVirtual)
            {
                label.text += "virtual ";
            }

            label.text += mi.ReturnParameter.ToString() + " ";
            label.text += mi.Name + "(";

            var parameterInfos = mi.GetParameters();
            for (var i = 0; i < parameterInfos.Length; i++)
            {
                var parameterInfo = parameterInfos[i];
                label.text += parameterInfo.ParameterType.ToString() + " ";
                label.text += parameterInfo.Name;
                if (i < parameterInfos.Length - 1)
                {
                    label.text += ",";
                }
            }
            label.text += ")\n";

            label.text += "\n";
            label.text += "Reflection Sample\n";
            label.text += "var methods = type.GetMethods (\n";
            label.text += "                                 BindingFlags.FlattenHierarchy\n";
            label.text += "                               | BindingFlags.Instance\n";

            if (mi.IsStatic)
            {
                label.text += "                               | BindingFlags.Static\n";
            }
            if (mi.IsPublic)
            {
                label.text += "                               | BindingFlags.Public\n";
            }
            else if (mi.IsPrivate)
            {
                label.text += "                               | BindingFlags.NonPublic\n";
            }
            label.text += ");\n";

        }

        
        // プロパティ用インフォメーション
        private void SetPropertyInfo(MemberListViewItem memberData, Label label)
        {
            var pi = memberData.memberInfo as PropertyInfo;
            label.text = "";
            label.text += pi.Name;
            label.text += " Property\n";
            label.text += pi.PropertyType.ToString() + " " + pi.Name + "\n";
            foreach (var am in pi.GetAccessors())
            {
                if (am.IsPrivate)
                {
                    label.text += "private ";
                }
                else if (am.IsFamilyAndAssembly)
                {
                    label.text += "private protected";
                }
                else if (am.IsFamilyOrAssembly)
                {
                    label.text += "protected internal";
                }
                else if (am.IsPublic)
                {
                    label.text += "public ";
                }
                else if (am.IsVirtual)
                {
                    label.text += "virtual ";
                }
                else if (am.IsFamily)
                {
                    label.text += " protected ";
                }

                if (am.IsStatic)
                {
                    label.text += "static ";
                }

                if (am.IsAbstract)
                {
                    label.text += "abstract ";
                }
                else if (am.IsFinal)
                {
                    label.text += "final ";
                }
                else if (am.IsVirtual)
                {
                    label.text += "virtual ";
                }

                label.text += " " + am + "\n";
            }
        }


        // フィールド用インフォメーション
        private void SetFieldInfo(MemberListViewItem memberData, Label label)
        {
            var fi = memberData.memberInfo as FieldInfo;
            label.text = "";
            label.text += fi.Name;
            label.text += " Field\n";
            label.text += "\n";

            if (fi.IsPrivate)
            {
                label.text += "private ";
            }
            else if (fi.IsFamilyAndAssembly)
            {
                label.text += "private protected";
            }
            else if (fi.IsFamilyOrAssembly)
            {
                label.text += "protected internal";
            }
            else if (fi.IsPublic)
            {
                label.text += "public ";
            }
            else if (fi.IsFamily)
            {
                label.text += " protected ";
            }

            if (fi.IsStatic)
            {
                label.text += "static ";
            }
            label.text += fi.FieldType + " " + fi.Name + ";\n";
            label.text += "\n";
            label.text += "var field = type.GetField(\"\n";
            label.text += "                                 BindingFlags.FlattenHierarchy\n";
            label.text += "                               | BindingFlags.Instance\n";
            if (fi.IsStatic)
            {
                label.text += "                               | BindingFlags.Static\n";
            }
            if (fi.IsPublic)
            {
                label.text += "                               | BindingFlags.Public\n";
            }
            else if (fi.IsPrivate)
            {
                label.text += "                               | BindingFlags.NonPublic\n";
            }
            label.text += ");\n";
        }
    }
}