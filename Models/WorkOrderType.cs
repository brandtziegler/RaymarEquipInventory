﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class WorkOrderType
{
    public int TypeId { get; set; }

    public DateTime DateTimeCreated { get; set; }

    public string TypeName { get; set; }

    public string IconName { get; set; }

    public string HexColor { get; set; }

    public virtual ICollection<WorkOrderStatus> WorkOrderStatuses { get; set; } = new List<WorkOrderStatus>();

    public virtual ICollection<WorkOrderStatus> Statuses { get; set; } = new List<WorkOrderStatus>();
}