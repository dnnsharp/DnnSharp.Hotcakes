using DnnSharp.Common.Actions;
using Hotcakes.Commerce;
using Hotcakes.Commerce.Catalog;
using Hotcakes.Commerce.Extensions;
using Hotcakes.Commerce.Orders;
using Hotcakes.Commerce.Taxes;
using PlantAnApp.Lib.Helpers.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DnnSharp.Hotcakes.Actions {
    public class AddProduct : IActionImpl {

        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public string ProductSku { get; set; }

        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public string ProductPrice { get; set; }

        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public string ProductQuantity { get; set; }

        /// <summary>
        /// Optional custom product name
        /// </summary>
        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public string ProductName { get; set; }

        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public string TaxRate { get; set; }

        /// <summary>
        /// Usefull to set a custom cart item description
        /// </summary>
        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public string ProductShortDescription { get; set; }

        /// <summary>
        /// The Key should be the choice name as set in Hotcakes, the Value should have be correct respective to choice type
        /// </summary>
        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public IDictionary<string, string> ProductChoices { get; set; }

        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public IDictionary<string, string> ProductCustomProperties { get; set; }

        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public string TaxScheduleName { get; set; }

        [ActionParameter(Scope = eActionParameterScope.Instance, ApplyTokens = true)]
        public bool IncreaseQuantityForSameProductSamePrice { get; set; }

        [ActionParameter(Scope = eActionParameterScope.Instance, IsOutputToken = true)]
        public string OutputTokenName { get; set; }

        public void Init(StringsDictionary actionTypeSettings, SettingsDictionary actionSettings) {

        }

        public IActionResult Execute(ActionContext context) {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "Hotcakes.Commerce")) {
                string orderBvin = AddProductToCart(context);
                if (string.IsNullOrWhiteSpace(OutputTokenName) == false)
                    context[OutputTokenName] = orderBvin;
            }
            return null;
        }

        void AddProductProperty(Product p, string name, string value) {
            if (p.CustomPropertyExists(Constants.DevId, name)) {
                p.CustomProperties.SetProperty(Constants.DevId, name, value);
            } else {
                p.CustomProperties.Add(Constants.DevId, name, value);
            }
        }

        void AddLineItemProperty(LineItem li, string name, string value) {
            if (li.CustomPropertyExists(Constants.DevId, name)) {
                li.CustomProperties.SetProperty(Constants.DevId, name, value);
            } else {
                li.CustomProperties.Add(Constants.DevId, name, value);
            }
        }

        OptionSelections GetOptionsFromDictionary(Product product, ActionContext context) {
            OptionSelections opts = new OptionSelections();
            foreach (KeyValuePair<string, string> property in ProductChoices) {
                string optionName = context.ApplyAllTokens(property.Key);
                string tempValue = context.ApplyAllTokens(property.Value);
                
                Option opt = product.Options.FirstOrDefault(x => x.Name.Trim() == optionName.Trim());
                if (opt != null) {
                    string optionValue = "";
                    if (opt.Items != null && opt.Items.Count > 0) {
                        OptionItem optionItem = opt.Items.FirstOrDefault(x => x.Name.Trim() == tempValue.Trim());
                        if (optionItem != null) {
                            optionValue = optionItem.Bvin;
                        }
                    } else {
                        optionValue = tempValue;
                    }
                    opts.OptionSelectionList.Add(new OptionSelection(opt.Bvin, optionValue));
                }
            }
            return opts;
        }

        string AddProductToCart(ActionContext context) {

            HotcakesApplication HccApp = HccAppHelper.InitHccApp();
            Product p = HccApp.CatalogServices.Products.FindBySku(ProductSku);
            if (p == null)
                throw new Exception("Product was not found in catalog!");

            // create order if it doesn't already exist
            OrderService orderService = HccApp.OrderServices;
            orderService.EnsureShoppingCart();
            Order order = orderService.CurrentShoppingCart();

            if (order == null)
                throw new Exception("Could not open order!");

            int qty = 1;
            int.TryParse(ProductQuantity, out qty);

            decimal price = -1;
            decimal.TryParse(ProductPrice, out price);

            LineItem li = p.ConvertToLineItem(HccApp, qty, GetOptionsFromDictionary(p, context), price == -1 ? null : (decimal?)price);

            // this overwrites the default product name
            if(string.IsNullOrWhiteSpace(ProductName) == false)
                li.ProductName = ProductName;

            if (IncreaseQuantityForSameProductSamePrice == false) {
                li.SelectionData.OptionSelectionList.Add(new OptionSelection("IncreaseQuantityForSame", Guid.NewGuid().ToString()));
            } else {
                // hack to fix Hotcakes bug (On entering new product in cart lineItem.SelectionData is compared to existing LineItem's SelectionData and for all found increasing quantity)
                // the bug is that OptionSelectionList.Equals actuals it is == EmptyOrEquals
                li.SelectionData.OptionSelectionList.Add(new OptionSelection("DnnSharpIntegration", ""));
            }

            decimal taxRate = 0;
            decimal.TryParse(TaxRate, out taxRate);

            // if tax exists then set it
            TaxSchedule tax = HccApp.OrderServices.TaxSchedules.FindByNameForThisStore(TaxScheduleName.Trim());
            if (tax != null) {
                //li.IsTaxExempt = false;
                li.TaxSchedule = tax.Id;
            }

            // set custom ProductShortDescription. Doing this overwrites the product description computed from product choices
            if(string.IsNullOrWhiteSpace(ProductShortDescription) == false)
                li.ProductShortDescription = ProductShortDescription;

            // set custom properties
            foreach (var prop in ProductCustomProperties) {
                AddLineItemProperty(li, prop.Key, prop.Value);
            }

            // add to order (cart) and calculate shipping and taxes
            HccApp.AddToOrderWithCalculateAndSave(order, li);
            
            SessionManager.SetCookieString("hotcakes-cartid-" + order.StoreId, order.bvin);

            // return order id:
            return order.bvin;
        }
    }
}
