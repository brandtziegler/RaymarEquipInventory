﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class VwWorkOrdBriefDetail
{
    public int WorkOrderNumber { get; set; }

    public int SheetId { get; set; }

    public DateTime DateTimeCreated { get; set; }

    public DateTime? DateTimeCompleted { get; set; }

    public DateTime? DateTimeStarted { get; set; }

    public int? StatusId { get; set; }

    public string WorkOrderStatus { get; set; }

    public int? TypeId { get; set; }

    public string TypeIconName { get; set; }

    public string TypeHexColor { get; set; }

    public string WorkOrderType { get; set; }

    public string IconName { get; set; }

    public string HexColor { get; set; }

    public string Pono { get; set; }

    public string Notes { get; set; }

    public string WorkLocation { get; set; }

    public int? CustomerId { get; set; }

    public string CustomerName { get; set; }

    public string ParentName { get; set; }

    public string FullAddress { get; set; }

    public string VehicleName { get; set; }
}