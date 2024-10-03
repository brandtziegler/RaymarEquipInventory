﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class WorkOrderSheet
{
    public int SheetId { get; set; }

    public int WorkOrderNumber { get; set; }

    public DateTime DateTimeCreated { get; set; }

    public string WorkOrdStatus { get; set; }

    public DateTime? DateTimeStarted { get; set; }

    public DateTime? DateTimeCompleted { get; set; }

    public virtual ICollection<BillingInformation> BillingInformations { get; set; } = new List<BillingInformation>();

    public virtual ICollection<MileageAndTime> MileageAndTimes { get; set; } = new List<MileageAndTime>();

    public virtual ICollection<PartsUsed> PartsUseds { get; set; } = new List<PartsUsed>();

    public virtual ICollection<TechnicianWorkOrder> TechnicianWorkOrders { get; set; } = new List<TechnicianWorkOrder>();

    public virtual ICollection<VehicleWorkOrder> VehicleWorkOrders { get; set; } = new List<VehicleWorkOrder>();
}