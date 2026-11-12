document.querySelectorAll('form[data-confirm]').forEach(form => {
 form.addEventListener('submit', event => {
  if (!window.confirm(form.dataset.confirm)) event.preventDefault();
 });
});
document.querySelectorAll('form[data-once]').forEach(form => {
 form.addEventListener('submit', () => {
  if (!form.checkValidity()) return;
  form.querySelectorAll('[type="submit"]').forEach(button => { button.disabled = true; button.dataset.previousText = button.textContent; button.textContent = 'Зберігаємо…'; });
 });
});
window.addEventListener('pageshow', () => {
 document.querySelectorAll('[data-previous-text]').forEach(button => { button.disabled = false; button.textContent = button.dataset.previousText; delete button.dataset.previousText; });
});

