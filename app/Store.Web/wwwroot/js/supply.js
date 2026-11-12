(() => {
 'use strict';
 const form=document.getElementById('supply-form');
 if(!form)return;
 const data=JSON.parse(document.getElementById('supply-data').textContent);
 const products=new Map(data.products.map(p=>[String(p.id),p]));
 const offers=new Map(data.supplierProducts.map(p=>[p.supplierId+':'+p.productId,p]));
 const supplier=document.getElementById('supplier-select');
 const lines=document.getElementById('supply-lines');
 const money=new Intl.NumberFormat('uk-UA',{minimumFractionDigits:2,maximumFractionDigits:2});
 const currency=StoreMoney.formatCurrency;
 const units={pcs:'шт.',kg:'кг',l:'л',m:'м'};
 const field=(row,name)=>row.querySelector('[data-field="'+name+'"]');
 function reindex(){
  lines.querySelectorAll('[data-row]').forEach((row,index)=>{
   row.querySelector('[data-number]').textContent=index+1;
   row.querySelectorAll('[data-field]').forEach(input=>{
    input.name='Input.Lines['+index+'].'+input.dataset.field;
    input.id='Input_Lines_'+index+'__'+input.dataset.field;
    const label=input.closest('.field')?.querySelector('label');
    if(label)label.htmlFor=input.id;
   });
  });
 }
 function fillOptions(row){
  const select=field(row,'ProductId'), selected=select.value;
  const placeholder=new Option(supplier.value?'Оберіть товар':'Спочатку оберіть постачальника','');
  const options=[placeholder];
  for(const offer of data.supplierProducts.filter(o=>String(o.supplierId)===supplier.value)){
   options.push(new Option(offer.productName,String(offer.productId)));
  }
  select.replaceChildren(...options);
  if(options.some(o=>o.value===selected))select.value=selected;
 }
 function refresh(){
  let total=0n;
  for(const row of lines.querySelectorAll('[data-row]')){
   const id=field(row,'ProductId').value;
   const product=products.get(id);
   const offer=offers.get(supplier.value+':'+id);
   const quantity=field(row,'Quantity'), cost=field(row,'UnitCost');
   quantity.step=product?.unit==='pcs'?'1':'0.001';
   quantity.min=product?.unit==='pcs'?'1':'0.001';
   row.querySelector('[data-product-note]').textContent=product?'Одиниця: '+(units[product.unit]||product.unit):'Асортимент обраного постачальника';
   row.querySelector('[data-price-note]').textContent=offer?'За прайсом: '+money.format(offer.purchasePrice)+' ₴':'Порожньо — ціна постачальника';
   const price=cost.value===''?(offer?.purchasePrice??0):cost.value;
   const value=product&&offer?StoreMoney.lineCents(quantity.value,price):0n;
   row.querySelector('[data-line-total]').textContent=currency(value);
   total+=value;
  }
  document.getElementById('supply-total').textContent=currency(total);
 }
 supplier.addEventListener('change',()=>{for(const row of lines.querySelectorAll('[data-row]'))fillOptions(row);refresh();});
 document.getElementById('add-supply-line').addEventListener('click',()=>{
  lines.append(document.getElementById('supply-line-template').content.cloneNode(true));
  fillOptions(lines.lastElementChild);reindex();refresh();
  field(lines.lastElementChild,'ProductId').focus();
 });
 form.addEventListener('input',refresh);
 form.addEventListener('change',refresh);
 form.addEventListener('click',event=>{
  const remove=event.target.closest('[data-remove]');
  if(remove){remove.closest('[data-row]').remove();reindex();refresh();}
 });
 for(const row of lines.querySelectorAll('[data-row]'))fillOptions(row);
 reindex();refresh();
})();

