﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class IncomeAccount
{
    public int IncomeAccountId { get; set; }

    public string AccountName { get; set; }

    public virtual ICollection<InventoryDatum> InventoryData { get; set; } = new List<InventoryDatum>();
}