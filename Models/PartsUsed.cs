﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class PartsUsed
{
    public int PartUsedId { get; set; }

    public int QtyUsed { get; set; }

    public string QuickBooksInvId { get; set; }

    public string Notes { get; set; }

    public int SheetId { get; set; }

    public virtual InventoryDatum QuickBooksInv { get; set; }

    public virtual WorkOrderSheet Sheet { get; set; }
}