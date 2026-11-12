(() => {
 'use strict';
 const form = document.getElementById('sale-form');
 if (!form) return;
 const data = JSON.parse(document.getElementById('sale-data').textContent);
 const products = new Map(data.products.map(p => [String(p.id), p]));
 const stock = new Map(data.stock.map(s => [s.departmentId + ':' + s.productId, s]));
 const lines = document.getElementById('sale-lines');
 const payments = document.getElementById('payment-lines');
 const money = new Intl.NumberFormat('uk-UA', {minimumFractionDigits:2, maximumFractionDigits:2});
 const number = new Intl.NumberFormat('uk-UA', {maximumFractionDigits:3});
 const units = {pcs:'шт.',kg:'кг',l:'л',m:'м'};
 const currency = StoreMoney.formatCurrency;
 const amount = input => Number.isFinite(input.valueAsNumber) ? input.valueAsNumber : 0;
 const field = (row, name) => row.querySelector('[data-field="' + name + '"]');
 let total = 0n;
 function reindex(container, prefix) {
  container.querySelectorAll('[data-row]').forEach((row, index) => {
   const label = row.querySelector('[data-number]');
   if (label) label.textContent = index + 1;
   row.querySelectorAll('[data-field]').forEach(input => {
    input.name = prefix + '[' + index + '].' + input.dataset.field;
    input.id = prefix.replaceAll('.', '_') + '_' + index + '__' + input.dataset.field;
    const caption = input.closest('.field')?.querySelector('label');
    if (caption) caption.htmlFor = input.id;
   });
  });
 }
 function syncLine(row) {
  const department = field(row,'DepartmentId').value;
  const select = field(row,'ProductId');
  const selected = select.value;
  for (const option of select.options) {
   const available = !option.value || stock.has(department + ':' + option.value);
   option.hidden = !available;
   option.disabled = !available;
  }
  if (selected && !stock.has(department + ':' + selected)) select.value = '';
  select.options[0].textContent = department ? 'Оберіть товар' : 'Спочатку оберіть відділ';
  const product = products.get(select.value);
  const available = stock.get(department + ':' + select.value);
  const quantity = field(row,'Quantity');
  quantity.step = product?.unit === 'pcs' ? '1' : '0.001';
  quantity.min = product?.unit === 'pcs' ? '1' : '0.001';
  const note = row.querySelector('[data-stock]');
  if (available && product) {
   note.textContent = 'Залишок: ' + number.format(available.quantity) + ' ' + (units[product.unit] || product.unit) + ' · ' + money.format(product.salePrice) + ' ₴ / ' + (units[product.unit] || product.unit);
   note.classList.toggle('text-danger', amount(quantity) > available.quantity);
  } else {
   note.textContent = department ? 'Оберіть товар із залишків цього відділу' : 'Оберіть товар і відділ';
   note.classList.remove('text-danger');
  }
  const base = product ? StoreMoney.lineCents(quantity.value, product.salePrice) : 0n;
  const discount = StoreMoney.cents(field(row,'DiscountAmount').value);
  const value = base - discount;
  row.querySelector('[data-line-total]').textContent = currency(value);
  row.querySelector('[data-line-total]').classList.toggle('text-danger',value < 0n);
  return {base,discount,value};
 }
 function refresh() {
  let subtotal = 0n, discount = 0n;
  for (const row of lines.querySelectorAll('[data-row]')) {
   const value = syncLine(row);
   subtotal += value.base; discount += value.discount;
  }
  total = subtotal - discount;
  const paid = Array.from(payments.querySelectorAll('[data-field="Amount"]')).reduce((sum,input)=>sum+StoreMoney.cents(input.value),0n);
  document.getElementById('subtotal').textContent = currency(subtotal);
  document.getElementById('discount-total').textContent = currency(discount);
  document.getElementById('sale-total').textContent = currency(total);
  document.getElementById('paid-total').textContent = currency(paid);
  const difference = total - paid;
  const balance = document.getElementById('payment-balance');
  const hasProducts = Array.from(lines.querySelectorAll('[data-field="ProductId"]')).some(input=>input.value);
  balance.classList.toggle('balanced',hasProducts && difference===0n && total>=0n);
  balance.classList.toggle('unbalanced',hasProducts && (difference!==0n || total<0n));
  balance.textContent = !hasProducts ? 'Додайте товари до чека.' : total < 0n ? 'Знижка перевищує вартість товарів.' : difference===0n ? 'Сума оплат відповідає вартості покупки.' : difference>0n ? 'Залишилося вказати: ' + currency(difference) : 'Сума оплат завелика на ' + currency(-difference);
 }
 function add(container, template, prefix) {
  container.append(document.getElementById(template).content.cloneNode(true));
  reindex(container,prefix); refresh();
  container.lastElementChild?.querySelector('select,input')?.focus();
 }
 document.getElementById('add-sale-line').addEventListener('click',()=>add(lines,'sale-line-template','Input.Lines'));
 document.getElementById('add-payment').addEventListener('click',()=>add(payments,'payment-template','Input.Payments'));
 form.addEventListener('input',refresh);
 form.addEventListener('change',refresh);
 form.addEventListener('click',event=>{
  const remove = event.target.closest('[data-remove]');
  if(remove){
   const row=remove.closest('[data-row]');
   const container=row.parentElement;
   row.remove(); reindex(container,container===lines?'Input.Lines':'Input.Payments'); refresh();
  }
  const fill=event.target.closest('[data-fill]');
  if(fill){
   const input=field(fill.closest('[data-row]'),'Amount');
   const others=Array.from(payments.querySelectorAll('[data-field="Amount"]')).filter(p=>p!==input).reduce((sum,p)=>sum+StoreMoney.cents(p.value),0n);
   const remaining=total-others;
   input.value=remaining>0n?StoreMoney.formatInput(remaining):'';
   refresh();
  }
 });
 reindex(lines,'Input.Lines'); reindex(payments,'Input.Payments'); refresh();
})();

