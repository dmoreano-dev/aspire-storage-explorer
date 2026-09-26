const API = '/api';
// Destructive requests must carry this header; the server rejects them otherwise.
const REQUEST_HEADERS = { 'X-Storage-Explorer': '1' };
const JSON_HEADERS = { ...REQUEST_HEADERS, 'Content-Type': 'application/json' };

const els = {
  containers: document.getElementById('containers'),
  containerFilter: document.getElementById('container-filter'),
  breadcrumb: document.getElementById('breadcrumb'),
  message: document.getElementById('message'),
  toolbar: document.getElementById('toolbar'),
  filter: document.getElementById('filter'),
  filterCount: document.getElementById('filter-count'),
  table: document.getElementById('entries'),
  sortHeaders: document.querySelectorAll('#entries th[data-sort]'),
  tbody: document.querySelector('#entries tbody'),
  status: document.getElementById('status'),
  refresh: document.getElementById('refresh'),
  dialog: document.getElementById('confirm-dialog'),
  dialogText: document.getElementById('confirm-text'),
  connection: document.getElementById('connection'),
  access: document.getElementById('access'),
  changeConnection: document.getElementById('change-connection'),
  connectionDialog: document.getElementById('connection-dialog'),
  connectionForm: document.getElementById('connection-form'),
  connectionString: document.getElementById('connection-string'),
  connectionShow: document.getElementById('connection-show'),
  connectionWrites: document.getElementById('connection-writes'),
  connectionAllowWrites: document.getElementById('connection-allow-writes'),
  connectionError: document.getElementById('connection-error'),
  connectionReset: document.getElementById('connection-reset'),
  connectionCancel: document.getElementById('connection-cancel'),
  connectionSubmit: document.getElementById('connection-submit'),
};

let connection = null;
let containers = [];
let renderToken = 0;
let statusTimer;

// The folder on screen, kept so that sorting and clearing the search do not need another request.
let listed = null;
let listedLocation = null;
// What the search box found in that folder and below it, or null while the box is empty.
let found = null;
let searchToken = 0;
let searchTimer;
let sort = { key: 'name', direction: 1 };

// Wait for a pause in typing before searching, since every search reads the listing from the account.
const SEARCH_DELAY_MS = 250;

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
const sortValues = {
  name: (entry) => entry.name,
  size: (entry) => entry.size,
  type: (entry) => entry.contentType,
  modified: (entry) => (entry.lastModified ? Date.parse(entry.lastModified) : null),
};

// --- Helpers -----------------------------------------------------------------

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function link(href, text, className) {
  const anchor = el('a', className, text);
  anchor.href = href;
  return anchor;
}

function safeDecode(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function formatSize(bytes) {
  if (bytes == null) return '';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(1)} ${units[unit]}`;
}

function formatDate(value) {
  return value ? new Date(value).toLocaleString() : '';
}

function showMessage(text, isError = false) {
  els.message.hidden = !text;
  els.message.textContent = text ?? '';
  els.message.className = isError ? 'message error' : 'message';
  els.message.setAttribute('role', isError ? 'alert' : 'status');
}

function setStatus(text) {
  els.status.textContent = text;
  clearTimeout(statusTimer);
  statusTimer = setTimeout(() => { els.status.textContent = ''; }, 5000);
}

// --- Location: kept in the URL hash as #/<container>/<prefix> -----------------

function parseLocation() {
  const raw = location.hash.replace(/^#\/?/, '');
  const slash = raw.indexOf('/');
  const container = safeDecode(slash === -1 ? raw : raw.slice(0, slash));
  const prefix = slash === -1 ? '' : raw.slice(slash + 1).split('/').map(safeDecode).join('/');
  return { container, prefix };
}

function hashFor(container, prefix = '') {
  const encodedPrefix = prefix.split('/').map(encodeURIComponent).join('/');
  return `#/${encodeURIComponent(container)}/${encodedPrefix}`;
}

// --- API -----------------------------------------------------------------------

async function api(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    let detail = response.statusText;
    try {
      const problem = await response.json();
      detail = problem.detail || problem.title || detail;
    } catch {
      // The body is not JSON; keep the status text.
    }
    throw new Error(`${response.status} ${detail}`.trim());
  }
  return response;
}

async function getJson(url) {
  return (await api(url)).json();
}

const containerUrl = (container) => `${API}/containers/${encodeURIComponent(container)}`;
const blobUrl = (container, path) => `${containerUrl(container)}/blob?path=${encodeURIComponent(path)}`;

// --- Rendering -----------------------------------------------------------------

const containerTerm = () => els.containerFilter.value.trim();

function matchingContainers() {
  const needle = containerTerm().toLowerCase();
  return containers.filter((container) => container.name.toLowerCase().includes(needle));
}

function renderContainers(active) {
  const matching = matchingContainers();
  const items = matching.map((container) => {
    const item = el('li');
    const anchor = link(hashFor(container.name), container.name);
    if (container.name === active) anchor.setAttribute('aria-current', 'page');
    item.append(anchor);
    return item;
  });

  if (containers.length > 0 && matching.length === 0) items.push(el('li', 'empty', 'No containers match.'));

  els.containerFilter.hidden = containers.length === 0;
  els.containers.replaceChildren(...items);
}

function renderBreadcrumb(container, prefix) {
  const parts = [link('#/', 'Containers')];

  if (container) {
    parts.push(link(hashFor(container), container));

    let accumulated = '';
    for (const segment of prefix.split('/').filter(Boolean)) {
      accumulated += `${segment}/`;
      parts.push(link(hashFor(container, accumulated), segment));
    }
  }

  els.breadcrumb.replaceChildren(
    ...parts.flatMap((part, index) => (index === 0 ? [part] : [el('span', 'sep', '/'), part])),
  );
}

function hideListing() {
  listed = null;
  found = null;
  els.table.hidden = true;
  els.toolbar.hidden = true;
}

// Folders stay on top whatever the column and the direction. They have no size, type or date, so they go by name.
function compareEntries(a, b) {
  if (a.isFolder !== b.isFolder) return a.isFolder ? -1 : 1;

  const key = a.isFolder ? 'name' : sort.key;
  const direction = key === sort.key ? sort.direction : 1;
  const left = sortValues[key](a);
  const right = sortValues[key](b);
  const byName = collator.compare(a.name, b.name);

  if (left == null || right == null) {
    // A missing value goes last in both directions.
    return left == null && right == null ? byName : left == null ? 1 : -1;
  }

  const result = typeof left === 'number' ? left - right : collator.compare(left, right);
  return result * direction || byName;
}

function renderSortHeaders() {
  for (const header of els.sortHeaders) {
    const active = header.dataset.sort === sort.key;
    if (active) {
      header.setAttribute('aria-sort', sort.direction === 1 ? 'ascending' : 'descending');
    } else {
      header.removeAttribute('aria-sort');
    }
    header.querySelector('.arrow').textContent = active ? (sort.direction === 1 ? '▲' : '▼') : '';
  }
}

function sortBy(key) {
  sort = { key, direction: sort.key === key ? -sort.direction : 1 };
  renderEntries();
}

const currentTerm = () => els.filter.value.trim();

// Shows the folder, or what the search box found in it and below it.
function renderEntries() {
  if (!listed) return;

  const { container, listing: folder } = listed;
  const listing = found ? found.listing : folder;
  const entries = [...listing.entries].sort(compareEntries);

  els.tbody.replaceChildren(...entries.map((entry) => renderRow(container, entry)));
  renderSortHeaders();

  els.table.hidden = entries.length === 0;
  els.toolbar.hidden = folder.entries.length === 0;
  els.filterCount.textContent = found ? `${entries.length} found` : '';

  if (folder.entries.length === 0) {
    showMessage('This folder is empty.');
  } else if (found) {
    const limit = listing.truncated ? 'The search stopped at its limit, so there may be more matches.' : '';
    showMessage(entries.length === 0 ? `No blobs match "${found.term}" in this folder or below. ${limit}`.trim() : limit);
  } else if (listing.truncated) {
    showMessage(`Listing truncated: showing the first ${entries.length} entries.`);
  } else {
    showMessage('');
  }
}

// Looks for the text of the search box in the folder on screen and in every folder below it.
async function search() {
  if (!listed) return;

  const token = ++searchToken;
  const term = currentTerm();
  if (!term) {
    found = null;
    renderEntries();
    return;
  }

  const { container, prefix } = listed;
  els.filterCount.textContent = 'Searching…';
  try {
    const listing = await getJson(
      `${containerUrl(container)}/search?prefix=${encodeURIComponent(prefix)}&q=${encodeURIComponent(term)}`);
    if (token !== searchToken) return;
    found = { term, listing };
    renderEntries();
  } catch (error) {
    if (token !== searchToken) return;
    found = null;
    renderEntries();
    showMessage(`Could not search "${container}": ${error.message}`, true);
  }
}

function renderRow(container, entry) {
  const row = el('tr');

  const name = el('td', 'name');
  name.append(el('span', 'icon', entry.isFolder ? '📁' : '📄'));
  if (entry.isFolder) {
    name.append(link(hashFor(container, entry.path), entry.name));
  } else {
    // A search result is named by its path below the folder searched: the folder part links to where the blob is.
    const slash = entry.name.lastIndexOf('/');
    if (slash !== -1) {
      const folder = entry.path.slice(0, entry.path.length - entry.name.length + slash + 1);
      name.append(link(hashFor(container, folder), entry.name.slice(0, slash + 1)));
    }
    name.append(el('span', undefined, entry.name.slice(slash + 1)));
  }

  const actions = el('td', 'actions');
  if (!entry.isFolder) {
    actions.append(link(blobUrl(container, entry.path), 'Download', 'button'));

    // Hiding the button is only a courtesy: the server refuses the delete on a read-only connection anyway.
    if (connection && !connection.readOnly) {
      const deleteButton = el('button', 'danger', 'Delete');
      deleteButton.type = 'button';
      deleteButton.addEventListener('click', () => deleteBlob(container, entry));
      actions.append(deleteButton);
    }
  }

  row.append(
    name,
    el('td', 'num', formatSize(entry.size)),
    el('td', undefined, entry.isFolder ? 'Folder' : entry.contentType ?? ''),
    el('td', undefined, formatDate(entry.lastModified)),
    actions,
  );
  return row;
}

async function render() {
  const token = ++renderToken;
  const { container, prefix } = parseLocation();

  // A search in flight is about a listing that is being replaced.
  searchToken++;
  clearTimeout(searchTimer);

  renderContainers(container);
  renderBreadcrumb(container, prefix);

  // A search applies to the folder it was typed in.
  const locationKey = `${container}/${prefix}`;
  if (locationKey !== listedLocation) {
    listedLocation = locationKey;
    els.filter.value = '';
  }

  if (!container) {
    hideListing();
    showMessage(containers.length === 0 ? 'No containers found.' : 'Select a container to browse its blobs.');
    return;
  }

  try {
    const listing = await getJson(`${containerUrl(container)}/entries?prefix=${encodeURIComponent(prefix)}`);
    if (token !== renderToken) return;
    listed = { container, prefix, listing };
    found = null;

    // After a refresh or a delete the search box may still have text: search again.
    if (currentTerm()) await search();
    else renderEntries();
  } catch (error) {
    if (token !== renderToken) return;
    hideListing();
    showMessage(`Could not list "${container}": ${error.message}`, true);
  }
}

function renderConnection() {
  els.connection.textContent = connection
    ? `${connection.accountName} · ${connection.endpoint}${connection.isCustom ? ' · custom' : ''}`
    : '';

  // Nothing to say for a local connection you can change: that is the normal case.
  const readOnly = connection?.readOnly ?? false;
  const risky = connection != null && !readOnly && !connection.isLocal;

  els.access.hidden = !readOnly && !risky;
  els.access.className = risky ? 'badge risk' : 'badge';
  els.access.textContent = readOnly ? '🔒 Read-only' : '🔓 Writable · own risk';
  if (readOnly) {
    els.access.title = connection.readOnlyLocked
      ? 'readOnly is set in WithStorageExplorer.'
      : 'This account is not on this machine. Use Change connection and allow changes to delete.';
  } else {
    els.access.title = 'This account is not on this machine and changes are allowed.';
  }
}

async function loadConnection() {
  try {
    connection = await getJson(`${API}/connection`);
  } catch {
    connection = null;
  }
  renderConnection();
}

async function loadContainers() {
  try {
    containers = await getJson(`${API}/containers`);
  } catch (error) {
    containers = [];
    showMessage(`Could not load containers: ${error.message}`, true);
  }
}

// --- Actions -------------------------------------------------------------------

function confirmDialog(text) {
  els.dialogText.textContent = text;
  els.dialog.returnValue = '';

  return new Promise((resolve) => {
    els.dialog.addEventListener('close', () => resolve(els.dialog.returnValue === 'confirm'), { once: true });
    els.dialog.showModal();
  });
}

async function deleteBlob(container, entry) {
  const account = connection ? ` in account "${connection.accountName}"` : '';
  const confirmed = await confirmDialog(`Delete "${entry.path}" from "${container}"${account}? This cannot be undone.`);
  if (!confirmed) return;

  try {
    await api(blobUrl(container, entry.path), { method: 'DELETE', headers: REQUEST_HEADERS });
    setStatus(`Deleted ${entry.path}`);
    await render();
  } catch (error) {
    showMessage(`Could not delete "${entry.path}": ${error.message}`, true);
  }
}

async function refresh() {
  await Promise.all([loadConnection(), loadContainers()]);
  await render();
}

// --- Connection dialog ---------------------------------------------------------

function showConnectionError(text) {
  els.connectionError.hidden = !text;
  els.connectionError.textContent = text ?? '';
}

function setConnectionBusy(busy) {
  els.connectionSubmit.disabled = busy;
  els.connectionReset.disabled = busy;
  els.connectionSubmit.textContent = busy ? 'Connecting…' : 'Connect';
}

function openConnectionDialog() {
  showConnectionError('');
  els.connectionShow.checked = false;
  els.connectionString.type = 'password';
  els.connectionAllowWrites.checked = false;
  els.connectionWrites.hidden = connection?.readOnlyLocked ?? false;
  els.connectionReset.hidden = !connection?.isCustom;
  els.connectionDialog.showModal();
  els.connectionString.focus();
}

// The account changed, so the containers and the current path no longer apply.
async function connectionChanged(info) {
  connection = info;
  els.containerFilter.value = '';
  els.connectionDialog.close();
  history.replaceState(null, '', '#/');
  setStatus(`Connected to ${info.accountName}`);
  await refresh();
}

async function sendConnection(options) {
  setConnectionBusy(true);
  showConnectionError('');
  try {
    const response = await api(`${API}/connection`, options);
    await connectionChanged(await response.json());
  } catch (error) {
    showConnectionError(error.message);
  } finally {
    setConnectionBusy(false);
  }
}

els.changeConnection.addEventListener('click', openConnectionDialog);
els.connectionCancel.addEventListener('click', () => els.connectionDialog.close());
els.connectionShow.addEventListener('change', () => {
  els.connectionString.type = els.connectionShow.checked ? 'text' : 'password';
});
// The secret must not linger in the page once the dialog closes, however it closes, and neither must the consent to
// write: it is asked again for every connection.
els.connectionDialog.addEventListener('close', () => {
  els.connectionString.value = '';
  els.connectionString.type = 'password';
  els.connectionAllowWrites.checked = false;
});
els.connectionForm.addEventListener('submit', (event) => {
  event.preventDefault();
  sendConnection({
    method: 'PUT',
    headers: JSON_HEADERS,
    body: JSON.stringify({
      connectionString: els.connectionString.value,
      allowWrites: els.connectionAllowWrites.checked,
    }),
  });
});
els.connectionReset.addEventListener('click', () => sendConnection({ method: 'DELETE', headers: REQUEST_HEADERS }));

els.containerFilter.addEventListener('input', () => renderContainers(parseLocation().container));
els.containerFilter.addEventListener('keydown', (event) => {
  // Enter opens the first container that matches.
  const [first] = matchingContainers();
  if (event.key === 'Enter' && containerTerm() && first) location.hash = hashFor(first.name);
});

els.filter.addEventListener('input', () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(search, currentTerm() ? SEARCH_DELAY_MS : 0);
});
for (const header of els.sortHeaders) {
  header.querySelector('button').addEventListener('click', () => sortBy(header.dataset.sort));
}

els.refresh.addEventListener('click', refresh);
window.addEventListener('hashchange', render);
refresh();
