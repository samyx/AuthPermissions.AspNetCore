﻿// Copyright (c) 2022 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuthPermissions.BaseCode.CommonCode;
using Example4.MvcWebApp.IndividualAccounts.Controllers;
using Example4.ShopCode.CacheCode;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Net.DistributedFileStoreCache;

namespace Example4.MvcWebApp.IndividualAccounts.Middleware;

public static class RegisterDownForMaintenance
{
    //Redirect constants

    public static readonly string MaintenanceAllAppDownRedirect = $"/{MaintenanceControllerName}/{nameof(MaintenanceController.ShowAllDownStatus)}";
    public static readonly string MaintenanceTenantDownRedirect = $"/{MaintenanceControllerName}/{nameof(MaintenanceController.ShowTenantDownStatus)}";

    //Various controller, actions, areas used to allow users to access these while in a down state
    public const string MaintenanceControllerName = "Maintenance";
    public const string AccountArea = "Identity";

    public static void AddDownForMaintenance(this WebApplication app)
    {
        app.Use(async (HttpContext context, Func<Task> next) =>
        {
            var all = context.GetRouteData();
            var controllerName = (string)context.GetRouteData().Values["controller"];
            var action = (string)context.GetRouteData().Values["action"];
            var area = (string)context.GetRouteData().Values["area"];
            if (controllerName == MaintenanceControllerName || area == AccountArea)
            {
                // This allows the Maintenance controller to show the banner and users to log in/out
                // The log in/out is there because if the user that set up the maintenance status logged out they wouldn't be able to log in again! 
                await next();
                return;
            }



            var fsCache = context.RequestServices.GetRequiredService<IDistributedFileStoreCacheClass>();
            var downCacheList = fsCache.GetAllKeyValues()
                .Where(x => x.Key.StartsWith(DownForStatusExtensions.DownForStatusPrefix))
                .Select(x => new KeyValuePair<string, string>(x.Key, x.Value))
                .ToList();

            var allDownData = fsCache.GetClassFromString<AllAppDownDto>(
                downCacheList.SingleOrDefault(x => x.Key == DownForStatusExtensions.DownForStatusAllAppDown).Value);
            if (allDownData != null)
            {
                //There is a "Down For Maintenance" in effect, so only the person that set up this state can still access the app

                var userId = context.User.GetUserIdFromUser();
                if (userId != allDownData.UserId)
                {
                    //The user isn't allowed to access the application 
                    context.Response.Redirect(MaintenanceAllAppDownRedirect);
                    return;
                }
            }

            var tenantDowns = downCacheList
                .Where(x => x.Key == DownForStatusExtensions.DownForStatusTenantUpdate)
                .ToList();
            if (tenantDowns.Any() && context.User.GetAuthDataKeyFromUser() != null)
            {
                //there are at least one tenant that shouldn't be accessed at this moment, and the current user is linked to a tenant.
                //Therefore we need to compare all the tenantDowns' Value, which contains the tenant's DataKey, with the user's DataKey

                var usersDataKey = context.User.GetAuthDataKeyFromUser();
                if (tenantDowns.Any(x => x.Value == usersDataKey))
                {
                    //This user isn't allowed to access the tenant at this time
                    context.Response.Redirect(MaintenanceTenantDownRedirect);
                    return;
                }
            }

            //If it gets to here, then the user is allowed to access the application and its databases
            await next();
        });
    }
}