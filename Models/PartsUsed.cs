﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class PartsUsed
{
    public int PartUsedId { get; set; }

    public int QtyUsed { get; set; }

    public string Notes { get; set; }

    public int SheetId { get; set; }

    public int? InventoryId { get; set; }

    public virtual InventoryDatum Inventory { get; set; }

    public virtual WorkOrderSheet Sheet { get; set; }
}