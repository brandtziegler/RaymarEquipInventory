﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class BillingInformation
{
    public int BillingId { get; set; }

    public int WorkOrderNumber { get; set; }

    public string Pono { get; set; }

    public int? UnitNo { get; set; }

    public int? Kilometers { get; set; }

    public int SheetId { get; set; }

    public int? BillingPersonId { get; set; }

    public virtual Person BillingPerson { get; set; }

    public virtual ICollection<ServiceDescription> ServiceDescriptions { get; set; } = new List<ServiceDescription>();

    public virtual WorkOrderSheet Sheet { get; set; }
}