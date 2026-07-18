using MapAssist.Helpers;
using MapAssist.Structs;
using System;

namespace MapAssist.Types
{
    public class UnitObject : UnitAny
    {
        public ObjectData ObjectData { get; private set; }
        private ObjectTxt ObjectText { get; set; }
        public GameObject GameObject => (GameObject)TxtFileNo;

        public UnitObject(IntPtr ptrUnit) : base(ptrUnit)
        {
        }

        public new UnitObject Update()
        {
            if (base.Update() == UpdateResult.Updated)
            {
                using (var processContext = GameManager.GetProcessContext())
                {
                    ObjectData = processContext.Read<ObjectData>(Struct.pUnitData);
                    if (ObjectData.pObjectTxt != IntPtr.Zero)
                    {
                        ObjectText = processContext.Read<ObjectTxt>(ObjectData.pObjectTxt);
                    }
                }
            }

            return this;
        }
        public bool IsStash
        {
            get
            {
                return UnitType == UnitType.Object &&
                       GameObject == GameObject.Bank;
            }
        }
        public bool IsPortal
        {
            get
            {
                var name = Enum.GetName(typeof(GameObject), GameObject);
                return ((!string.IsNullOrWhiteSpace(name) && name.Contains("Portal") && GameObject != GameObject.WaypointPortal) || GameObject == GameObject.HellGate);
            }
        }

        public bool IsWaypoint => GameObject.IsWaypoint();

        public bool IsShrine => UnitType == UnitType.Object && ObjectData.pShrineTxt != IntPtr.Zero && ObjectData.InteractType <= (byte)ShrineType.Poison;

        public bool IsWell => UnitType == UnitType.Object && ObjectData.pObjectTxt != IntPtr.Zero && ObjectText.ObjectType == "Well";

        public bool IsChest => UnitType == UnitType.Object && ObjectData.pObjectTxt != IntPtr.Zero && Struct.Mode == 0 && Chest.NormalChests.Contains(GameObject);

        public bool IsArmorWeapRack => UnitType == UnitType.Object && ObjectData.pObjectTxt != IntPtr.Zero && Struct.Mode == 0 && Chest.ArmorWeapRacks.Contains(GameObject);

        public override string HashString => GameObject + "/" + Position.X + "/" + Position.Y;

        public override string GetInfo()
        {
            return base.GetInfo() +
                   " GameObject=" + GameObject +
                   " InteractType=" + ObjectData.InteractType +
                   " PortalFlags=" + ObjectData.PortalFlags +
                   " pObjectTxt=0x" + ObjectData.pObjectTxt.ToInt64().ToString("X") +
                   " pShrineTxt=0x" + ObjectData.pShrineTxt.ToInt64().ToString("X") +
                   " IsStash=" + IsStash +
                   " IsPortal=" + IsPortal +
                   " IsWaypoint=" + IsWaypoint +
                   " IsShrine=" + IsShrine +
                   " IsWell=" + IsWell +
                   " IsChest=" + IsChest +
                   " IsArmorWeapRack=" + IsArmorWeapRack;
        }
    }
}
