﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class VwWorkOrderCard
{
    public int SheetId { get; set; }

    public int WorkOrderNumber { get; set; }

    public string WorkDescription { get; set; }

    public string WorkOrderStatus { get; set; }

    public DateTime? DateUploaded { get; set; }

    public DateTime? DateTimeCompleted { get; set; }

    public string UnitNo { get; set; }

    public int? CustomerId { get; set; }

    public string PathToRoot { get; set; }

    public string ParentCustomerName { get; set; }

    public string ChildCustomerName { get; set; }

    public string LastSyncEventType { get; set; }

    public DateTime? LastSyncTimestamp { get; set; }
}