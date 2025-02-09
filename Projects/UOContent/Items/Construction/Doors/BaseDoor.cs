using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Targeting;

namespace Server.Items;

[SerializationGenerator(0, false)]
public abstract partial class BaseDoor : Item, ILockable, ITelekinesisable
{
    private static readonly Point3D[] m_Offsets =
    {
        new(-1, 1, 0),
        new(1, 1, 0),
        new(-1, 0, 0),
        new(1, -1, 0),
        new(1, 1, 0),
        new(1, -1, 0),
        new(0, 0, 0),
        new(0, -1, 0),

        new(0, 0, 0),
        new(0, 0, 0),
        new(0, 0, 0),
        new(0, 0, 0)
    };

    private Timer _timer;

    [SerializableField(0)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private uint _keyValue;

    [SerializableField(2)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private bool _locked;

    [SerializableField(3)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _openedId;

    [SerializableField(4)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _closedId;

    [SerializableField(5)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _openedSound;

    [SerializableField(6)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _closedSound;

    [SerializableField(7)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private Point3D _offset;

    [SerializableField(8)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private BaseDoor _link;

    public BaseDoor(int closedID, int openedID, int openedSound, int closedSound, Point3D offset) : base(closedID)
    {
        _openedId = openedID;
        _closedId = closedID;
        _openedSound = openedSound;
        _closedSound = closedSound;
        _offset = offset;

        _timer = new InternalTimer(this);

        Movable = false;
    }

    [SerializableProperty(1)]
    [CommandProperty(AccessLevel.GameMaster)]
    public bool Open
    {
        get => _open;
        set
        {
            if (_open != value)
            {
                _open = value;

                ItemID = _open ? _openedId : _closedId;

                if (_open)
                {
                    Location = new Point3D(X + _offset.X, Y + _offset.Y, Z + _offset.Z);
                }
                else
                {
                    Location = new Point3D(X - _offset.X, Y - _offset.Y, Z - _offset.Z);
                }

                Effects.PlaySound(this, _open ? OpenedSound : ClosedSound);

                if (_open)
                {
                    _timer ??= new InternalTimer(this);
                    _timer.Start();
                }
                else
                {
                    _timer.Stop();
                    _timer = null;
                }

                this.MarkDirty();
            }
        }
    }

    public virtual bool UseChainedFunctionality => false;

    public void OnTelekinesis(Mobile from)
    {
        Effects.SendLocationParticles(EffectItem.Create(Location, Map, EffectItem.DefaultDuration), 0x376A, 9, 32, 5022);
        Effects.PlaySound(Location, Map, 0x1F5);

        Use(from);
    }

    public static void Initialize()
    {
        EventSink.OpenDoorMacroUsed += EventSink_OpenDoorMacroUsed;

        CommandSystem.Register("Link", AccessLevel.GameMaster, Link_OnCommand);
        CommandSystem.Register("ChainLink", AccessLevel.GameMaster, ChainLink_OnCommand);
    }

    [Usage("Link"), Description("Links two targeted doors together.")]
    private static void Link_OnCommand(CommandEventArgs e)
    {
        e.Mobile.BeginTarget(-1, false, TargetFlags.None, Link_OnFirstTarget);
        e.Mobile.SendMessage("Target the first door to link.");
    }

    private static void Link_OnFirstTarget(Mobile from, object targeted)
    {
        if (targeted is not BaseDoor door)
        {
            from.BeginTarget(-1, false, TargetFlags.None, Link_OnFirstTarget);
            from.SendMessage("That is not a door. Try again.");
        }
        else
        {
            from.BeginTarget(-1, false, TargetFlags.None, Link_OnSecondTarget, door);
            from.SendMessage("Target the second door to link.");
        }
    }

    private static void Link_OnSecondTarget(Mobile from, object targeted, BaseDoor first)
    {
        if (targeted is not BaseDoor second)
        {
            from.BeginTarget(-1, false, TargetFlags.None, Link_OnSecondTarget, first);
            from.SendMessage("That is not a door. Try again.");
        }
        else
        {
            first.Link = second;
            second.Link = first;
            from.SendMessage("The doors have been linked.");
        }
    }

    [Usage("ChainLink"), Description("Chain-links two or more targeted doors together.")]
    private static void ChainLink_OnCommand(CommandEventArgs e)
    {
        e.Mobile.BeginTarget(-1, false, TargetFlags.None, ChainLink_OnTarget, new List<BaseDoor>());
        e.Mobile.SendMessage("Target the first of a sequence of doors to link.");
    }

    private static void ChainLink_OnTarget(Mobile from, object targeted, List<BaseDoor> list)
    {
        if (targeted is not BaseDoor door)
        {
            from.BeginTarget(-1, false, TargetFlags.None, ChainLink_OnTarget, list);
            from.SendMessage("That is not a door. Try again.");
        }
        else
        {
            if (list.Count > 0 && list[0] == door)
            {
                if (list.Count >= 2)
                {
                    for (var i = 0; i < list.Count; ++i)
                    {
                        list[i].Link = list[(i + 1) % list.Count];
                    }

                    from.SendMessage("The chain of doors have been linked.");
                }
                else
                {
                    from.BeginTarget(-1, false, TargetFlags.None, ChainLink_OnTarget, list);
                    from.SendMessage("You have not yet targeted two unique doors. Target the second door to link.");
                }
            }
            else if (list.Contains(door))
            {
                from.BeginTarget(-1, false, TargetFlags.None, ChainLink_OnTarget, list);
                from.SendMessage(
                    "You have already targeted that door. Target another door, or retarget the first door to complete the chain."
                );
            }
            else
            {
                list.Add(door);

                from.BeginTarget(-1, false, TargetFlags.None, ChainLink_OnTarget, list);

                if (list.Count == 1)
                {
                    from.SendMessage("Target the second door to link.");
                }
                else
                {
                    from.SendMessage("Target another door to link. To complete the chain, retarget the first door.");
                }
            }
        }
    }

    private static void EventSink_OpenDoorMacroUsed(Mobile m)
    {
        if (m.Map == null)
        {
            return;
        }

        int x = m.X, y = m.Y;

        switch (m.Direction & Direction.Mask)
        {
            case Direction.North:
                {
                    --y;
                    break;
                }
            case Direction.Right:
                {
                    ++x;
                    --y;
                    break;
                }
            case Direction.East:
                {
                    ++x;
                    break;
                }
            case Direction.Down:
                {
                    ++x;
                    ++y;
                    break;
                }
            case Direction.South:
                {
                    ++y;
                    break;
                }
            case Direction.Left:
                {
                    --x;
                    ++y;
                    break;
                }
            case Direction.West:
                {
                    --x;
                    break;
                }
            case Direction.Up:
                {
                    --x;
                    --y;
                    break;
                }
        }

        var sector = m.Map.GetSector(x, y);

        foreach (var item in sector.Items)
        {
            if (item.Location.X == x && item.Location.Y == y && item.Z + item.ItemData.Height > m.Z &&
                m.Z + 16 > item.Z && item is BaseDoor && m.CanSee(item) && m.InLOS(item))
            {
                if (m.CheckAlive())
                {
                    m.SendLocalizedMessage(500024); // Opening door...
                    item.OnDoubleClick(m);
                }

                break;
            }
        }
    }

    public static Point3D GetOffset(DoorFacing facing) => m_Offsets[(int)facing];

    public bool CanClose()
    {
        if (!_open)
        {
            return true;
        }

        var map = Map;

        if (map == null)
        {
            return false;
        }

        var p = new Point3D(X - Offset.X, Y - Offset.Y, Z - Offset.Z);

        return CheckFit(map, p, 16);
    }

    private bool CheckFit(Map map, Point3D p, int height)
    {
        if (map == Map.Internal)
        {
            return false;
        }

        var x = p.X;
        var y = p.Y;
        var z = p.Z;

        var sector = map.GetSector(x, y);
        var mobs = sector.Mobiles;

        foreach (var item in sector.Items)
        {
            if (item is not BaseMulti && item.ItemID <= TileData.MaxItemValue && item.AtWorldPoint(x, y) &&
                item is not BaseDoor)
            {
                var id = item.ItemData;
                var surface = id.Surface;
                var impassable = id.Impassable;

                if ((surface || impassable) && item.Z + id.CalcHeight > z && z + height > item.Z)
                {
                    return false;
                }
            }
        }

        for (var i = 0; i < mobs.Count; ++i)
        {
            var m = mobs[i];

            if (m.Location.X == x && m.Location.Y == y)
            {
                if (m.Hidden && m.AccessLevel > AccessLevel.Player)
                {
                    continue;
                }

                if (!m.Alive)
                {
                    continue;
                }

                if (m.Z + 16 > z && z + height > m.Z)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public List<BaseDoor> GetChain()
    {
        var list = new List<BaseDoor>();
        var c = this;

        do
        {
            list.Add(c);
            c = c.Link;
        } while (c?.Deleted == false && !list.Contains(c));

        return list;
    }

    public bool IsFreeToClose()
    {
        if (!UseChainedFunctionality)
        {
            return CanClose();
        }

        var list = GetChain();

        var freeToClose = true;

        for (var i = 0; freeToClose && i < list.Count; ++i)
        {
            freeToClose = list[i].CanClose();
        }

        return freeToClose;
    }

    public virtual bool IsInside(Mobile from) => false;

    public virtual bool UseLocks() => true;

    public virtual void Use(Mobile from)
    {
        if (Locked && !_open && UseLocks())
        {
            if (from.AccessLevel >= AccessLevel.GameMaster)
            {
                from.LocalOverheadMessage(
                    MessageType.Regular,
                    0x3B2,
                    502502
                ); // That is locked, but you open it with your godly powers.
                // from.Send( new MessageLocalized( Serial, ItemID, MessageType.Regular, 0x3B2, 3, 502502, "", "" ) ); // That is locked, but you open it with your godly powers.
            }
            else if (Key.ContainsKey(from.Backpack, KeyValue))
            {
                from.LocalOverheadMessage(
                    MessageType.Regular,
                    0x3B2,
                    501282
                ); // You quickly unlock, open, and relock the door
            }
            else if (IsInside(from))
            {
                from.LocalOverheadMessage(
                    MessageType.Regular,
                    0x3B2,
                    501280
                ); // That is locked, but is usable from the inside.
            }
            else
            {
                if (Hue == 0x44E && Map == Map.Malas) // doom door into healer room in doom
                {
                    SendLocalizedMessageTo(from, 1060014); // Only the dead may pass.
                }
                else
                {
                    from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 502503); // That is locked.
                }

                return;
            }
        }

        if (_open && !IsFreeToClose())
        {
            return;
        }

        if (_open)
        {
            OnClosed(from);
        }
        else
        {
            OnOpened(from);
        }

        if (UseChainedFunctionality)
        {
            var open = !_open;

            var list = GetChain();

            for (var i = 0; i < list.Count; ++i)
            {
                list[i].Open = open;
            }
        }
        else
        {
            Open = !_open;

            var link = Link;

            if (_open && link is { Deleted: false, Open: false })
            {
                link.Open = true;
            }
        }
    }

    public virtual void OnOpened(Mobile from)
    {
    }

    public virtual void OnClosed(Mobile from)
    {
    }

    public override void OnDoubleClick(Mobile from)
    {
        if (from.AccessLevel == AccessLevel.Player && !from.InRange(GetWorldLocation(), 2))
        {
            from.LocalOverheadMessage(MessageType.Regular, 0x3B2, 1019045); // I can't reach that.
        }
        else
        {
            Use(from);
        }
    }

    [AfterDeserialization]
    private void AfterDeserialization()
    {
        if (_open)
        {
            _timer = new InternalTimer(this);
            _timer.Start();
        }
    }

    private class InternalTimer : Timer
    {
        private readonly BaseDoor _door;

        public InternalTimer(BaseDoor door) : base(TimeSpan.FromSeconds(20.0), TimeSpan.FromSeconds(10.0)) =>
            _door = door;

        protected override void OnTick()
        {
            if (_door.Open && _door.IsFreeToClose())
            {
                _door.Open = false;
            }
        }
    }
}
