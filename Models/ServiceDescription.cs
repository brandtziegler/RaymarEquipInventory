﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace RaymarEquipmentInventory.Models;

public partial class ServiceDescription
{
    public int ServiceId { get; set; }

    public int BillingId { get; set; }

    public string ServiceDescription1 { get; set; }

    public int? Quantity { get; set; }

    public decimal? Cost { get; set; }

    public virtual BillingInformation Billing { get; set; }
}