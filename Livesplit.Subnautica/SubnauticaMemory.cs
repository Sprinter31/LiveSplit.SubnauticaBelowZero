using LiveSplit.ComponentUtil;
using LiveSplit.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Voxif.AutoSplitter;
using Voxif.Helpers.Unity;
using Voxif.IO;
using Voxif.Memory;

namespace Livesplit.SubnauticaBelowZero
{
    public class SubnauticaMemory : Memory
    {
        protected override string[] ProcessNames => new string[] { "SubnauticaZero" };

        public InventoryItem CurrentItemToCheck { get; set; }
        public Unlockable CurrentBlueprintToCheck { get; set; }
        public EncyEntry CurrentEncyEntryToCheck { get; set; }      

        private IMonoHelper mono;

        public bool startedTimerBefore = false;
        public bool isInMainMenu = false;
        public bool pointersInitialized;
        public GameVersion gameVersion;
        //string[] EncyMappingMarch2023;

        public readonly Dictionary<SplitName, Func<bool>> splitConditions;

        private SubnauticaSettings settings;

        #region Pointer stuff
        public Pointer<double> TimePassed;
        public Pointer<bool> PlayerInputEnabled;
        public Pointer<bool> IsAnimationPlaying;
        public Pointer<bool> RocketLaunching;
        public Pointer<float> Health;
        public Pointer<IntPtr> MainMenu;
        public Pointer<IntPtr> knowntechPtr;
        public Pointer<IntPtr> pdaMappingPtr;
        public Pointer<int> PDATab;
        public Pointer<int> GameMode;
        public Pointer<IntPtr> completedGoalsPtr;
        public StringPointer Biome;


        public Dictionary<TechType, int> PlayerInventory = new Dictionary<TechType, int>();
        public Dictionary<TechType, int> PlayerInventoryOld = new Dictionary<TechType, int>();
        public List<TechType> KnownTech = new List<TechType>();
        public List<TechType> KnownTechOld = new List<TechType>();
        public List<EncyEntry> Encyclopedia = new List<EncyEntry>();
        public List<EncyEntry> EncyclopediaOld = new List<EncyEntry>();

        IntPtr invKlass;
        IntPtr icKlass;
        int off_container;
        int off_itemsDict;
        IntPtr itemGroupKlass;
        int off_itemGroup_items;
        int off_list_size;
        int dict_off_entries;
        int arr_off_len;
        int arr_data_base;
        int off_itemGroup_id;
        int dict_off_version;
        int dict_off_count;
        int off_entry_unlocked;

        IntPtr invStaticKlass;
        int invStaticOffset;

        struct LegacyDictOffsets
        {
            public int off_table;
            public int off_linkSlots;
            public int off_keySlots;
            public int off_valSlots;
            public int off_touched;
        }
        bool useLegacyDict = false;
        LegacyDictOffsets legacy_off;

        public MemoryWatcher<bool> isLoadingScreen = new MemoryWatcher<bool>(IntPtr.Zero);
        public MemoryWatcher<bool> isNotInWater = new MemoryWatcher<bool>(IntPtr.Zero);
        public MemoryWatcher<int> isFabiOpen = new MemoryWatcher<int>(IntPtr.Zero); // 2 means that the esc menu is open
        public MemoryWatcher<float> walkDir = new MemoryWatcher<float>(IntPtr.Zero);
        public MemoryWatcher<float> strafeDir = new MemoryWatcher<float>(IntPtr.Zero);
        public MemoryWatcher<float> posX = new MemoryWatcher<float>(IntPtr.Zero);
        public MemoryWatcher<float> posY = new MemoryWatcher<float>(IntPtr.Zero);
        public MemoryWatcher<float> posZ = new MemoryWatcher<float>(IntPtr.Zero);
        #endregion

        private UnityHelperTask unityTask;

        public SubnauticaMemory(LiveSplitState state, SubnauticaComponent component, Logger logger, SubnauticaSettings settings) : base(logger)
        {            
            //EncyMappingMarch2023 = Assembly.GetExecutingAssembly().ReadAllLinesFromResource("Livesplit.Subnautica.Resources.EncyMappingMarch2023.txt");

            OnHook += () =>
            {
                GetGameVersion();
                unityTask = new UnityHelperTask(game, logger);
                unityTask.Run(InitPointers);               
            };

            OnExit += () => {
                if (unityTask != null)
                {
                    pointersInitialized = false;
                    unityTask.Dispose();
                    unityTask = null;
                }
            };

            this.settings = settings;
            
            splitConditions = new Dictionary<SplitName, Func<bool>>
            {
                { SplitName.Inventory,            () => PlayerInventory.GetCount(CurrentItemToCheck.ConvertTo<TechType>()) > PlayerInventoryOld.GetCount(CurrentItemToCheck.ConvertTo<TechType>()) },
                { SplitName.Blueprint,            () => KnownTech.Contains(CurrentBlueprintToCheck.ConvertTo<TechType>()) && !KnownTechOld.Contains(CurrentBlueprintToCheck.ConvertTo<TechType>()) },
                { SplitName.Encyclopedia,         () => Encyclopedia.Contains(CurrentEncyEntryToCheck) && !EncyclopediaOld.Contains(CurrentEncyEntryToCheck) },
            };
        }

        public override bool Update()
        {           
            if(!pointersInitialized)
                return base.Update();

            UpdateMemoryWatchers();

            isInMainMenu = IsInMainMenu();
            if (isInMainMenu)
                startedTimerBefore = false;
            foreach (var g in ReadCompletedGoals())
                logger.Log("Goal: " + g);

            return base.Update();
        }

        #region Memory stuff
        private void GetGameVersion()
        {
            System.Diagnostics.ProcessModule firstModule = game.Process.Modules.Cast<System.Diagnostics.ProcessModule>().FirstOrDefault();
            if (firstModule == null) return;
            int moduleLen = firstModule.ModuleMemorySize;
            switch (moduleLen)
            {
                case 671744:
                    gameVersion = GameVersion.Aug2021;
                    break;

                case 675840:
                    gameVersion = GameVersion.Oct2025;
                    break;

                default:
                    gameVersion = GameVersion.Oct2025;
                    MessageBox.Show($"Module length {moduleLen} does not match a version, defaulting to most recent (October 2025)",
                                    "Subnautica Autosplitter",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                    break;
            }
        }

        private void InitPointers(IMonoHelper mono)
        {
            this.mono = mono;
            var ptrFactory = new MonoNestedPointerFactory(game, mono);

            #region Intro
            TimePassed = ptrFactory.Make<double>("DayNightCycle", "main", "timePassedAsDouble");
            Pointer<IntPtr> playerControllerPtr = ptrFactory.Make<IntPtr>("Player", "main", "<playerController>k__BackingField");
            //PlayerInputEnabled = ptrFactory.Make<bool>(playerControllerPtr, mono.GetFieldOffset("PlayerController", "inputEnabled"));
            Pointer<IntPtr> activeControllerPtr = ptrFactory.Make<IntPtr>(playerControllerPtr, mono.GetFieldOffset("PlayerController", "activeController"));
            PlayerInputEnabled = ptrFactory.Make<bool>(activeControllerPtr, mono.GetFieldOffset("PlayerMotor", "canControl"));
            #endregion Intro
            #region Is Animation Playing
            IsAnimationPlaying = ptrFactory.Make<bool>("Player", "main", "_cinematicModeActive");
            #endregion Is Animation Playing
            #region Health
            Pointer<IntPtr> liveMixingPtr = ptrFactory.Make<IntPtr>("Player", "main", "liveMixin");
            IntPtr lmKlass = mono.FindClass("LiveMixin");
            int off_health = mono.GetFieldOffset(lmKlass, "health");
            Health = ptrFactory.Make<float>(liveMixingPtr, off_health);
            #endregion
            #region Inventory
            invKlass = mono.FindClass("Inventory", mono.MainImage);
            icKlass = mono.FindClass("ItemsContainer", mono.MainImage);

            invStaticOffset = mono.GetFieldOffset(invKlass, "main");
            invStaticKlass = invKlass;

            off_container = ((UnityHelperTask.UnityHelperBase)mono)
                .ResolveFieldOffsetByNameOrPredicate(invKlass, new[] { "_container" },
                    fname => UnityHelperTask.UnityNameUtil.NameHas(fname, "container"));

            off_itemsDict = ((UnityHelperTask.UnityHelperBase)mono)
                .ResolveFieldOffsetByNameOrPredicate(icKlass, new[] { "_items" },
                    fname => UnityHelperTask.UnityNameUtil.NameHas(fname, "items"));

            itemGroupKlass = mono.FindClass("ItemGroup", mono.MainImage);

            off_itemGroup_items = (itemGroupKlass != IntPtr.Zero)
                ? mono.GetFieldOffset(itemGroupKlass, "items") : 0;
            if (off_itemGroup_items == 0 && itemGroupKlass != IntPtr.Zero)
                off_itemGroup_items = ((UnityHelperTask.UnityHelperBase)mono)
                    .ResolveFieldOffsetByNameOrPredicate(itemGroupKlass, new[] { "items" },
                        fname => UnityHelperTask.UnityNameUtil.NameHas(fname, "items"));

            off_itemGroup_id = (itemGroupKlass != IntPtr.Zero)
                ? mono.GetFieldOffset(itemGroupKlass, "id") : 0;
            if (off_itemGroup_id == 0 && itemGroupKlass != IntPtr.Zero)
                off_itemGroup_id = ((UnityHelperTask.UnityHelperBase)mono)
                    .ResolveFieldOffsetByNameOrPredicate(itemGroupKlass, new[] { "id", "techType" },
                        fname => {
                            var f = fname.ToLowerInvariant();
                            return f == "id" || f.Contains("techtype") || f.EndsWith("techtype");
                        });


            // List<T>._size
            IntPtr core = ((UnityHelperTask.UnityHelperBase)mono).TryFindImageOnce(
                "mscorlib", "mscorlib.dll", "System.Private.CoreLib", "System.Private.CoreLib.dll", "netstandard", "netstandard.dll");
            IntPtr listKlass = core != IntPtr.Zero ? ((UnityHelperTask.UnityHelperBase)mono).TryFindClassOnce("List`1", core) : IntPtr.Zero;
            off_list_size = 0x18;
            if (listKlass != IntPtr.Zero)
            {
                int cand = mono.GetFieldOffset(listKlass, "_size");
                if (cand != 0) off_list_size = cand;
            }


            // Dictionary<>.entries, Array header
            dict_off_entries = 0x18;
            arr_off_len = 0x18;
            arr_data_base = 0x20;

            if (core != IntPtr.Zero)
            {
                IntPtr dictKlass = ((UnityHelperTask.UnityHelperBase)mono).TryFindClassOnce("Dictionary`2", core);
                if (dictKlass != IntPtr.Zero)
                {
                    var unity = (UnityHelperTask.UnityHelperBase)mono;

                    // modern path only for 2023
                    int oe = mono.GetFieldOffset(dictKlass, "entries");
                    if (oe == 0) oe = mono.GetFieldOffset(dictKlass, "_entries");
                    if (oe != 0) dict_off_entries = oe;
                    useLegacyDict = false;
                }
            }
            #endregion Inventory
            #region Known Tech
            knowntechPtr = ptrFactory.Make<IntPtr>("KnownTech", "knownTech");
            #endregion Known Tech
            #region PDA Mapping
            pdaMappingPtr = ptrFactory.Make<IntPtr>("PDAEncyclopedia", "entries");
            //off_entry_unlocked = gameVersion == GameVersion.Sept2018 ? 0x49 : 0x4C;
            #endregion PDA Mapping
            #region Main Menu
            MainMenu = ptrFactory.Make<IntPtr>("uGUI_MainMenu", "main");
            #endregion
            #region Biome
            Biome = ptrFactory.MakeString("Player", "main", "biomeString", 0x14);
            #endregion
            #region PDATab
            PDATab = ptrFactory.Make<int>("uGUI_PDA", "<main>k__BackingField", "tabOpen");
            #endregion PDATab
            #region Game Mode
            if (gameVersion == GameVersion.Aug2021)
                GameMode = ptrFactory.Make<int>("GameModeUtils", "currentGameMode");
            else
                GameMode = ptrFactory.Make<int>("GameModeManager", "currentPresetId");
            #endregion Game Mode
            completedGoalsPtr = ptrFactory.Make<IntPtr>("Story.StoryGoalManager", "<main>k__BackingField", "completedGoals");
            #region Memory Watchers
            DeepPointer loadingScreenPtr;
            DeepPointer portalLoadingPtr;
            DeepPointer hatchPtr;
            DeepPointer notInWaterPtr;
            DeepPointer fabiPtr;
            DeepPointer walkDirPtr;
            DeepPointer strafePtr;
            DeepPointer posXPtr;
            DeepPointer posYPtr;
            DeepPointer posZPtr;

            switch (gameVersion)
            {
                case GameVersion.Aug2021:
                    loadingScreenPtr = new DeepPointer("mono.dll", 0x266180, 0x50, 0x2C0, 0x0, 0x30, 0x8, 0x18, 0x20, 0x10, 0x44);
                    portalLoadingPtr = new DeepPointer("Subnautica.exe", 0x142B740, 0x8, 0x10, 0x30, 0x1F8, 0x28, 0x28);
                    hatchPtr = new DeepPointer("fmodstudio.dll", 0x304A30, 0x88, 0x18, 0x158, 0x498, 0x108);
                    notInWaterPtr = new DeepPointer("Subnautica.exe", 0x14BC6A0, 0x7C);
                    fabiPtr = new DeepPointer("mono.dll", 0x296BC8, 0x20, 0xA58, 0x20);
                    walkDirPtr = new DeepPointer("Subnautica.exe", 0x142B8C8, 0x158, 0x40, 0xA0);
                    strafePtr = new DeepPointer("Subnautica.exe", 0x142B8C8, 0x158, 0x40, 0x160);
                    posXPtr = new DeepPointer("UnityPlayer.dll", 0x17B84D8, 0x150, 0xBF8);
                    posYPtr = new DeepPointer("UnityPlayer.dll", 0x17B84D8, 0x150, 0xBFC);
                    posZPtr = new DeepPointer("UnityPlayer.dll", 0x17B84D8, 0x150, 0xC00);                   
                    break;

                default: // GameVersion.Oct2025
                    loadingScreenPtr = new DeepPointer("UnityPlayer.dll", 0x18AB2E0, 0x430, 0x8, 0x10, 0x48, 0x30, 0x7AC);
                    portalLoadingPtr = new DeepPointer("UnityPlayer.dll", 0x17FBE70, 0x10, 0x10, 0x30, 0x1F8, 0x28, 0x28);
                    hatchPtr = new DeepPointer("fmodstudio.dll", 0x2CED70, 0x78, 0x18, 0x190, 0x4D8, 0xB0, 0x20, 0x28);
                    notInWaterPtr = new DeepPointer("UnityPlayer.dll", 0x18AB130, 0x48, 0x0, 0x68);
                    fabiPtr = new DeepPointer("UnityPlayer.dll", 0x183BF48, 0x8, 0x10, 0x30, 0x30, 0x28, 0x128);
                    walkDirPtr = new DeepPointer("UnityPlayer.dll", 0x17FBC28, 0x30, 0x98);
                    strafePtr = new DeepPointer("UnityPlayer.dll", 0x17FBC28, 0x30, 0x150);
                    posXPtr = new DeepPointer("fmodstudio.dll", 0x2CED70, 0xE0, 0x8, 0x20, 0x48C);
                    posYPtr = new DeepPointer("fmodstudio.dll", 0x2CED70, 0xE0, 0x8, 0x20, 0x490);
                    posZPtr = new DeepPointer("fmodstudio.dll", 0x2CED70, 0xE0, 0x8, 0x20, 0x494);
                    break;
            }

            isLoadingScreen = new MemoryWatcher<bool>(loadingScreenPtr);
            isNotInWater = new MemoryWatcher<bool>(notInWaterPtr);
            isFabiOpen = new MemoryWatcher<int>(fabiPtr);
            walkDir = new MemoryWatcher<float>(walkDirPtr);
            strafeDir = new MemoryWatcher<float>(strafePtr);
            posX = new MemoryWatcher<float>(posXPtr);
            posY = new MemoryWatcher<float>(posYPtr);
            posZ = new MemoryWatcher<float>(posZPtr);
            #endregion Memory Watchers 

            logger.Log("Pointers initialized");
            pointersInitialized = true;
        }

        private void UpdateMemoryWatchers()
        {
            if (settings.creativeStart)
            {
                walkDir.Update(game.Process);
                strafeDir.Update(game.Process);
                isFabiOpen.Update(game.Process);
                isLoadingScreen.Update(game.Process);
            }

            if (settings.reset)
                UpdatePosition();

            if (Needs(SplitName.Inventory))
                UpdateInventory();

            if (Needs(SplitName.Blueprint))
                UpdateBlueprints();

            if(Needs(SplitName.Encyclopedia))
                UpdateEncyclopedia();
        }
        private void UpdatePosition() { posX.Update(game.Process); posY.Update(game.Process); posZ.Update(game.Process); }
        private bool Needs(params SplitName[] required) => required.Any(r => settings.Splits.Select(s => s.SplitName).Contains(r));
        #endregion Memory stuff

        #region World/Player Checks

        public bool IsInMainMenu() => posX.Current == 0 && posZ.Current == 0 && posY.Current == 1.75f;

        private void UpdateBlueprints()
        {
            List<TechType> blueprints = new List<TechType>();
            IntPtr startAddr = knowntechPtr.New;

            int slotsOffset = gameVersion == GameVersion.Aug2021 ? 0x20 : 0x18;
            IntPtr slots = game.Process.ReadPointer(startAddr + slotsOffset);
            int countOffset = gameVersion == GameVersion.Aug2021 ? 0x40 : 0x30;
            int count = game.Process.ReadValue<int>(startAddr + countOffset);

            int slotBeginningOffset = 0x20;
            int slotSize = gameVersion == GameVersion.Aug2021 ? 0x4 : 0xC;
            for (int i = 0; i < count; i++)
            {
                int tech = game.Process.ReadValue<int>(slots + slotBeginningOffset + slotSize * i);
                if (tech > 0 && tech < 10005)
                {
                    TechType type = (TechType)tech;
                    blueprints.Add(type);
                }
            }

            KnownTechOld = KnownTech;
            KnownTech = blueprints;
        }
        
        private void UpdateInventory()
        {
            PlayerInventoryOld = PlayerInventory;
            PlayerInventory = ReadInventoryCounts();
        }

        private void UpdateEncyclopedia()
        {
            EncyclopediaOld = Encyclopedia;
            Encyclopedia = ReadPDAEncyMapping();
        }

        private bool IsWithinBounds(float[] bounds)
        {
            float x = posX.Current;
            float y = posY.Current;
            float z = posZ.Current;
            if (x >= Math.Min(bounds[0], bounds[1]) && x <= Math.Max(bounds[0], bounds[1]) &&
                y >= Math.Min(bounds[2], bounds[3]) && y <= Math.Max(bounds[2], bounds[3]) &&
                z >= Math.Min(bounds[4], bounds[5]) && z <= Math.Max(bounds[4], bounds[5]))
                return true;
            else
                return false;
        }
        
        Dictionary<TechType, int> ReadInventoryCounts()
        {
            var result = new Dictionary<TechType, int>();

            IntPtr invMain = game.Read<IntPtr>(mono.GetStaticAddress(invStaticKlass) + invStaticOffset);
            if (invMain == IntPtr.Zero) return result;

            IntPtr container = game.Read<IntPtr>(invMain + off_container);
            if (container == IntPtr.Zero) return result;

            IntPtr dict = game.Read<IntPtr>(container + off_itemsDict);
            if (dict == IntPtr.Zero) return result;

            // modern layout (entries/_entries)
            if (!useLegacyDict)
            {
                // up to 3 tries to get a consistent snapshot
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int verBefore = (dict_off_version != 0) ? game.Read<int>(dict + dict_off_version) : 0;

                    IntPtr entriesArr = game.Read<IntPtr>(dict + dict_off_entries);
                    if (entriesArr == IntPtr.Zero) break;

                    int len = game.Read<int>(entriesArr + arr_off_len);
                    if (len <= 0 || len > 200000) break;

                    IntPtr basePtr = entriesArr + arr_data_base;

                    const int stride = 24;


                    // [0x00]=hashCode(int), [0x04]=next(int), [0x08]=key(int), [0x10]=value(ref)
                    for (int i = 0; i < len; i++)
                    {
                        IntPtr entry = basePtr + i * stride;

                        int hashCode = game.Read<int>(entry + 0x00);
                        if (hashCode < 0) continue;

                        int keyInt = game.Read<int>(entry + 0x08);
                        IntPtr pGroup = game.Read<IntPtr>(entry + 0x10);
                        if (pGroup == IntPtr.Zero) continue;

                        int id = (off_itemGroup_id != 0) ? game.Read<int>(pGroup + off_itemGroup_id) : keyInt;
                        if (id != keyInt) continue;

                        IntPtr pList = game.Read<IntPtr>(pGroup + off_itemGroup_items);
                        if (pList == IntPtr.Zero) continue;

                        int count = game.Read<int>(pList + off_list_size);
                        if ((uint)count > 100000) continue;

                        result[(TechType)keyInt] = count;
                    }

                    int verAfter = (dict_off_version != 0) ? game.Read<int>(dict + dict_off_version) : verBefore;
                    if (verAfter == verBefore)
                    {
                        return result;
                    }

                    result.Clear();
                }
            }

            // legacy layout (Unity 2018 mscorlib: keySlots/valueSlots[/linkSlots])
            if (!useLegacyDict)
            {
                //logger.Log("[Unity] Falling back to legacy Dictionary<> read (entries array missing).");
            }

            IntPtr keyArr = legacy_off.off_keySlots != 0 ? game.Read<IntPtr>(dict + legacy_off.off_keySlots) : IntPtr.Zero;
            IntPtr valArr = legacy_off.off_valSlots != 0 ? game.Read<IntPtr>(dict + legacy_off.off_valSlots) : IntPtr.Zero;
            if (valArr == IntPtr.Zero) return result;

            IntPtr linkArr = IntPtr.Zero;
            if (legacy_off.off_linkSlots != 0)
                linkArr = game.Read<IntPtr>(dict + legacy_off.off_linkSlots);

            // bounds
            int touched = 0;
            if (legacy_off.off_touched != 0)
                touched = game.Read<int>(dict + legacy_off.off_touched);

            int keyLen = keyArr != IntPtr.Zero ? game.Read<int>(keyArr + arr_off_len) : 0;
            int valLen = game.Read<int>(valArr + arr_off_len);
            int upper = valLen;

            if (touched > 0 && touched <= valLen) upper = touched;
            if (upper <= 0 || upper > 200000) return result;

            IntPtr keyBase = keyArr != IntPtr.Zero ? keyArr + arr_data_base : IntPtr.Zero;
            IntPtr valBase = valArr + arr_data_base;
            IntPtr linkBase = linkArr != IntPtr.Zero ? linkArr + arr_data_base : IntPtr.Zero;

            int ptrSize = IntPtr.Size;
            const int linkStride = 8;

            for (int i = 0; i < upper; i++)
            {
                if (linkBase != IntPtr.Zero)
                {
                    int h = game.Read<int>(linkBase + i * linkStride);
                    if (h == 0) continue;
                }

                IntPtr pGroup = game.Read<IntPtr>(valBase + i * ptrSize);
                if (pGroup == IntPtr.Zero) continue;

                int id = (off_itemGroup_id != 0) ? game.Read<int>(pGroup + off_itemGroup_id) : 0;

                int keyInt = id;
                if (keyBase != IntPtr.Zero)
                {
                    int k = game.Read<int>(keyBase + i * 4);
                    if (k == id) keyInt = k;
                }

                IntPtr pList = game.Read<IntPtr>(pGroup + off_itemGroup_items);
                if (pList == IntPtr.Zero) continue;

                int count = game.Read<int>(pList + off_list_size);
                if ((uint)count > 100000) continue;

                if (keyInt != 0)
                    result[(TechType)keyInt] = count;
            }
            return result;
        }

        public List<EncyEntry> ReadPDAEncyMapping()
        {
            var result = new List<EncyEntry>();

            IntPtr dict = pdaMappingPtr.New;
            if (dict == IntPtr.Zero)
                return result;

            int strHeader = game.PointerSize * 2 + 0x4;
            int ptrSize = game.PointerSize;

            // modern layout (entries/_entries)
            if (!useLegacyDict)
            {
                // up to 3 tries to get a consistent snapshot
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int verBefore = (dict_off_version != 0) ? game.Read<int>(dict + dict_off_version) : 0;

                    IntPtr entriesArr = game.Read<IntPtr>(dict + dict_off_entries);
                    if (entriesArr == IntPtr.Zero)
                        break;

                    int len = game.Read<int>(entriesArr + arr_off_len);
                    if (len <= 0 || len > 200000)
                        break;

                    IntPtr basePtr = entriesArr + arr_data_base;
                    const int stride = 24;

                    // [0x00]=hashCode(int), [0x04]=next(int), [0x08]=key(int), [0x10]=value(ref)
                    for (int i = 0; i < len; i++)
                    {
                        IntPtr entry = basePtr + i * stride;

                        int hashCode = game.Read<int>(entry + 0x00);
                        if (hashCode < 0)
                            continue;

                        IntPtr pKey = game.Read<IntPtr>(entry + 0x08);
                        IntPtr pVal = game.Read<IntPtr>(entry + 0x10); // PDAEncyclopedia.EntryData*
                        if (pKey == IntPtr.Zero || pVal == IntPtr.Zero)
                            continue;

                        string key = game.ReadString(pKey + strHeader, EStringType.UTF16Sized);
                        if (!string.IsNullOrEmpty(key))
                            if (Enum.TryParse(key, out EncyEntry encyEntry)) 
                                result.Add(encyEntry);
                    }

                    int verAfter = (dict_off_version != 0) ? game.Read<int>(dict + dict_off_version) : verBefore;
                    if (verAfter == verBefore)
                        return result;

                    result.Clear();
                }
            }

            // legacy layout (Unity 2018 mscorlib: keySlots/valueSlots[/linkSlots])
            {
                IntPtr keyArr = legacy_off.off_keySlots != 0 ? game.Read<IntPtr>(dict + legacy_off.off_keySlots) : IntPtr.Zero;
                IntPtr valArr = legacy_off.off_valSlots != 0 ? game.Read<IntPtr>(dict + legacy_off.off_valSlots) : IntPtr.Zero;
                if (valArr == IntPtr.Zero)
                    return result;

                IntPtr linkArr = IntPtr.Zero;
                if (legacy_off.off_linkSlots != 0)
                    linkArr = game.Read<IntPtr>(dict + legacy_off.off_linkSlots);

                int touched = 0;
                if (legacy_off.off_touched != 0)
                    touched = game.Read<int>(dict + legacy_off.off_touched);

                int keyLen = keyArr != IntPtr.Zero ? game.Read<int>(keyArr + arr_off_len) : 0;
                int valLen = game.Read<int>(valArr + arr_off_len);
                int upper = valLen;

                if (touched > 0 && touched <= valLen) upper = touched;
                if (upper <= 0 || upper > 200000) return result;

                IntPtr keyBase = keyArr != IntPtr.Zero ? keyArr + arr_data_base : IntPtr.Zero;
                IntPtr valBase = valArr + arr_data_base;
                IntPtr linkBase = linkArr != IntPtr.Zero ? linkArr + arr_data_base : IntPtr.Zero;

                const int linkStride = 8;

                for (int i = 0; i < upper; i++)
                {
                    if (linkBase != IntPtr.Zero)
                    {
                        int h = game.Read<int>(linkBase + i * linkStride);
                        if (h == 0) continue;
                    }

                    IntPtr pVal = game.Read<IntPtr>(valBase + i * ptrSize);
                    if (pVal == IntPtr.Zero) continue;

                    if (keyBase != IntPtr.Zero)
                    {
                        IntPtr pKey = game.Read<IntPtr>(keyBase + i * ptrSize);
                        if (pKey == IntPtr.Zero) continue;

                        string key = game.ReadString(pKey + strHeader, EStringType.UTF16Sized);
                        if (!string.IsNullOrEmpty(key))
                            if (Enum.TryParse(key, out EncyEntry encyEntry))
                                result.Add(encyEntry);
                    }
                }
            }

            return result;
        }

        private List<string> ReadCompletedGoals()
        {
            var result = new List<string>();

            IntPtr hs = completedGoalsPtr.New;
            if (hs == IntPtr.Zero)
                return result;

            var unity = (UnityHelperTask.UnityHelperBase)mono;
            int off_slots = unity.PickSlotsOffset(hs);
            if (off_slots == 0)
                return result;
                              

            IntPtr slotsArr = game.Process.ReadPointer(hs + off_slots);
            if (slotsArr == IntPtr.Zero)
                return result;

            int len = game.Process.ReadValue<int>(slotsArr + arr_off_len);
            if (len <= 0 || len > 200_000)
                return result;

            IntPtr basePtr = slotsArr + arr_data_base;

            int[] strides = { 16, 24 };
            int[] valOffs = { 0x08, 0x10 };

            int ptrSize = game.PointerSize;
            int lenOff = ptrSize * 2;
            int dataOff = lenOff + 4;

            for (int m = 0; m < strides.Length; m++)
            {
                int stride = strides[m];
                int valueOff = valOffs[m];
                int found = 0;

                for (int i = 0; i < len; i++)
                {
                    IntPtr slot = basePtr + i * stride;

                    int hashCode = game.Process.ReadValue<int>(slot + 0x00);
                    if (hashCode < 0)
                        continue;

                    IntPtr strPtr = game.Process.ReadPointer(slot + valueOff);
                    if (strPtr == IntPtr.Zero)
                        continue;

                    int sLen = game.Process.ReadValue<int>(strPtr + lenOff);
                    if (sLen <= 0 || sLen > 4096)
                        continue;

                    byte[] bytes = Voxif.Memory.ExtensionMethods.ReadBytes(game.Process, strPtr + dataOff, sLen * 2);
                    if (bytes == null || bytes.Length == 0)
                        continue;

                    string s = Encoding.Unicode.GetString(bytes);
                    if (!string.IsNullOrEmpty(s))
                    {
                        result.Add(s);
                        found++;
                    }
                }

                if (found > 0)
                    break;
            }

            return result;
        }
        #endregion

        #region Bounds
        // xmin, xmax, ymin, ymax, zmin, zmax
        private readonly float[] teethBounds = { -212f, 27f, -100f, 100f, 159f, 177f };
        private readonly float[] auroraExitBounds = { 545f, 550f, -10f, 10f, -265f, 256f };
        private readonly float[] mountainBounds = { 475f, 534f, -510f, -191f, 745f, 810f };
        private readonly float[] PCFEntrBounds = { 216f, 224f, -1453f, -1445f, -276f, -267f };
        private readonly float[] portalBounds = { 240f, 250f, -1590f, -1580f, -2000f, 2000f };
        private readonly float[] gunBounds = { 359f, 365f, -75f, -66f, 1079f, 1085f };
        private readonly float[] upperTabletBounds = { 380f, 386f, 10f, 30f, 1084f, 1090f };
        private readonly float[] SGLBaseBounds = { 20f, 80f, -45f, -17f, 290f, 360f };
        private readonly float[] deathClipABounds = { 33f, 65f, -20f, -8f, 118f, 96f };
        private readonly float[] deathClipCBounds = { -155f, -133f, -20f, -10f, 73f, 96f };
        private readonly float[] enterClipABounds = { 48f, 55f, -20f, -5f, 106f, 111f };
        private readonly float[] enterClipCBounds = { -142f, -132f, -20f, -5f, 82f, 90f };
        #endregion
    }
}