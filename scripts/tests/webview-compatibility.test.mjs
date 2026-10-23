import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const shimSource = fs.readFileSync(new URL('../../CodexVsix/UI/CodexWebview/codex-acquire-vscode-api-shim.js', import.meta.url), 'utf8');
const pathAccessor = shimSource.match(/getPathForFile:\s*(function\s*\(file\)\s*\{[\s\S]*?\n\s*\})/);
assert.ok(pathAccessor, 'Locate the shipped clipboard/drop path adapter');
const getPathForFile = vm.runInNewContext('(' + pathAccessor[1] + ')');

const assets = new URL('../../CodexVsix/UI/CodexWebview/webview/assets/', import.meta.url);
const source = name => fs.readFileSync(new URL(name, assets), 'utf8');
const composer = source('composer-B3BCMq_W.js');

const managerSource = source('app-server-manager-signals-B8yJv95l.js');
const imageStart = managerSource.indexOf('function fl(');
const imageEnd = managerSource.indexOf('function pl(', imageStart);
assert.ok(imageStart >= 0 && imageEnd > imageStart);
const imageTransport = vm.createContext({});
vm.runInContext(managerSource.slice(imageStart, imageEnd), imageTransport);

test('pasted images use their pixel data, never a filename as a local path', () => {
  const dataUrl = 'data:image/png;base64,AQID';
  const path = getPathForFile({ name: 'image.png', type: 'image/png' });
  assert.equal(path, null);
  const input = imageTransport.fl(dataUrl, { localPath: path });
  assert.equal(input.type, 'image');
  assert.equal(input.url, dataUrl);
  assert.equal(getPathForFile({ fsPath: 'D:\\fotos\\ação.png' }), 'D:\\fotos\\ação.png');
  assert.equal(getPathForFile(null), null);
});

test('the shipped picker classifies and reads images instead of attaching text mentions', async () => {
  const shared = source('src-DqZHIysr.js');
  const start = shared.indexOf('xm=[`png`');
  const end = shared.indexOf('function Em(', start);
  assert.ok(start >= 0 && end > start);
  const classifier = vm.createContext({});
  vm.runInContext('var ' + shared.slice(start, end), classifier);
  const pickerSource = source('thread-side-panel-tabs-Des13tNg.js');
  const pickerStart = pickerSource.indexOf('function cd(e){let t=[],n=[];');
  const pickerEnd = pickerSource.indexOf('var md=', pickerStart);
  assert.ok(pickerStart >= 0 && pickerEnd > pickerStart);
  const png = { label: 'ação 中文.png', path: 'D:/fotos/ação 中文.png', fsPath: 'D:/fotos/ação 中文.png' };
  const text = { label: 'notes.txt', path: 'D:/notes.txt', fsPath: 'D:/notes.txt' };
  const picker = vm.createContext({
    _: classifier.wm, F: 'local', rd: { lookup: () => 'image/png' },
    De: async (method, { params }) => {
      assert.equal(method, 'read-file-binary');
      assert.equal(params.path, png.fsPath);
      return { contentsBase64: 'AQID' };
    },
  });
  vm.runInContext(pickerSource.slice(pickerStart, pickerEnd), picker);
  const classified = picker.cd([png, text]);
  assert.equal(classified.images.length, 1);
  assert.equal(classified.others[0], text);
  const attachment = await picker.fd(classified.images[0]);
  assert.equal(attachment.dataUrl, 'data:image/png;base64,AQID');
  assert.equal(attachment.filename, png.label);
  const input = imageTransport.fl(attachment.dataUrl, { localPath: attachment.localPath });
  assert.equal(input.type, 'localImage');
  assert.equal(input.path, png.fsPath); // The host's tested normalizer supplies the bytes.
});
const labelsStart = composer.indexOf('function tg(');
const labelsEnd = composer.indexOf('var ng=', labelsStart);
assert.ok(labelsStart >= 0 && labelsEnd > labelsStart, 'Locate the shipped reasoning formatter');
const formatter = vm.createContext({});
vm.runInContext(composer.slice(labelsStart, labelsEnd), formatter);

test('the shipped composer labels all runtime efforts without changing existing labels', () => {
  const expected = { none: 'None', minimal: 'Minimal', low: 'Low', medium: 'Medium', high: 'High', xhigh: 'Extra High', max: 'Max', ultra: 'Ultra' };
  for (const [effort, label] of Object.entries(expected)) {
    assert.equal(formatter.tg({ formatMessage: message => message.defaultMessage }, effort), label);
  }
  assert.equal(formatter.tg({ formatMessage: message => message.defaultMessage }, 'future-effort'), 'future-effort');
});

test('Max and Ultra use an existing reasoning icon instead of an undefined component', () => {
  const start = composer.indexOf('var qp=');
  const end = composer.indexOf('function Yp(', start);
  assert.ok(start >= 0 && end > start);
  const icons = vm.createContext(Object.fromEntries(['Aa', 'Oa', 'Ea', 'wa', 'Sa'].map(name => [name, name])));
  vm.runInContext(composer.slice(start, end), icons);
  assert.equal(icons.Jp('max'), icons.Jp('xhigh'));
  assert.equal(icons.Jp('ultra'), icons.Jp('xhigh'));
  assert.equal(icons.Jp('low'), 'Oa');
});

const catalogSource = source('models-and-reasoning-efforts-DZT8Iz86.js');
const catalog = vm.createContext({});
vm.runInContext(catalogSource.slice(0, catalogSource.indexOf('export{')), catalog);
const newModel = {
  model: 'gpt-6.1-sol', displayName: 'GPT-6.1 Sol', isDefault: true, hidden: false,
  defaultReasoningEffort: 'low', supportedReasoningEfforts: [{ reasoningEffort: 'low' }, { reasoningEffort: 'ultra' }],
  availabilityNux: { title: 'A new model is available' }, upgradeInfo: { title: 'Upgrade' },
};

test('dynamic model discovery preserves announcement, upgrade and capability metadata', () => {
  const result = catalog.e({ authMethod: 'chatgpt', availableModels: new Set(), defaultModel: 'gpt-5.5', models: [newModel], useHiddenModels: false });
  assert.equal(result.models[0].availabilityNux, newModel.availabilityNux);
  assert.equal(result.models[0].upgradeInfo, newModel.upgradeInfo);
  assert.equal(result.defaultModel, newModel);
  assert.equal(result.models[0].supportedReasoningEfforts[1].reasoningEffort, 'ultra');
});

const settingsSource = source('use-model-settings-D_RJ6qrG.js');
const settingsStart = settingsSource.indexOf('function z(');
const settingsEnd = settingsSource.indexOf('var V=', settingsStart);
assert.ok(settingsStart >= 0 && settingsEnd > settingsStart);
const settings = vm.createContext({});
vm.runInContext(settingsSource.slice(settingsStart, settingsEnd), settings);

test('saved models are retained and effort validation follows the selected model', () => {
  const oldModel = { ...newModel, model: 'gpt-5.6-sol', isDefault: false };
  const luna = { ...newModel, model: 'gpt-6-luna', defaultReasoningEffort: 'medium', supportedReasoningEfforts: [{ reasoningEffort: 'medium' }, { reasoningEffort: 'max' }] };
  const listModelsData = { models: [newModel, oldModel, luna], defaultModel: newModel };
  const old = settings.B({ userSavedModelString: oldModel.model, userSavedReasoningEffort: 'ultra', listModelsData });
  assert.equal(old.model, oldModel.model);
  assert.equal(old.reasoningEffort, 'ultra');
  const bounded = settings.B({ userSavedModelString: luna.model, userSavedReasoningEffort: 'ultra', listModelsData });
  assert.equal(bounded.model, luna.model);
  assert.equal(bounded.reasoningEffort, 'medium');
  const custom = settings.B({ userSavedModelString: 'company-model', userSavedReasoningEffort: 'high', listModelsData });
  assert.equal(custom.model, 'company-model');
});

const menuLabelsSource = source('reasoning-minimal-BmczWw15.js');
const menuLabelsStart = menuLabelsSource.indexOf('function d(');
const menuLabelsEnd = menuLabelsSource.indexOf('var f=', menuLabelsStart);
assert.ok(menuLabelsStart >= 0 && menuLabelsEnd > menuLabelsStart, 'Locate the label component imported as Da by the chat selector');
const menuLabels = vm.createContext({
  u: { c: size => Array(size).fill(Symbol.for('react.memo_cache_sentinel')) },
  o: { jsx: (type, props) => ({ type, props }) },
  i: 'FormattedMessage',
});
vm.runInContext(menuLabelsSource.slice(menuLabelsStart, menuLabelsEnd), menuLabels);

const normalizationStart = settingsSource.indexOf('function R(');
const normalizationEnd = settingsSource.indexOf('function z(', normalizationStart);
assert.ok(normalizationStart >= 0 && normalizationEnd > normalizationStart);
const chatSelector = vm.createContext({ k: 'medium', Zr: catalog.r });
vm.runInContext(settingsSource.slice(normalizationStart, normalizationEnd), chatSelector);
chatSelector.Hr = chatSelector.R;
const selectorStart = composer.indexOf('function Yp(');
const selectorEnd = composer.indexOf('function Zp(', selectorStart);
assert.ok(selectorStart >= 0 && selectorEnd > selectorStart);
assert.match(composer, /o as Da[^;]+reasoning-minimal-BmczWw15\.js/);
assert.match(composer, /children:A\.map\(e=>\{let\{reasoningEffort:t\}=e;/);
assert.match(composer, /children:\(0,Q\.jsx\)\(Da,\{effort:t\}\)/);
vm.runInContext(composer.slice(selectorStart, selectorEnd), chatSelector);

test('the actual chat menu renders and retains every runtime effort for Sol and Astra', () => {
  for (const model of ['gpt-6.1-sol', 'gpt-6-astra']) {
    const efforts = ['low', 'medium', 'high', 'xhigh', 'max', 'ultra'];
    const models = [{ ...newModel, model, supportedReasoningEfforts: efforts.map(reasoningEffort => ({ reasoningEffort })) }];
    const entries = chatSelector.Yp(models, model);
    assert.deepEqual(Array.from(entries, entry => entry.reasoningEffort), efforts);
    for (const { reasoningEffort } of entries) {
      const rendered = menuLabels.d({ effort: reasoningEffort });
      assert.equal(rendered.type, 'FormattedMessage');
      assert.equal(rendered.props.id, `composer.mode.local.reasoning.${reasoningEffort}.label`);
      assert.equal(chatSelector.Xp(reasoningEffort, entries), reasoningEffort, 'The selected effort survives the actual chat validator');
    }
    assert.equal(menuLabels.d({ effort: 'max' }).props.defaultMessage, 'Max');
    assert.equal(menuLabels.d({ effort: 'ultra' }).props.defaultMessage, 'Ultra');
    assert.equal(menuLabels.d({ effort: 'xhigh' }).props.defaultMessage, 'Extra High');
  }
});

test('chat effort normalization respects each model and future runtime values', () => {
  const luna = [{ ...newModel, model: 'gpt-6-luna', supportedReasoningEfforts: ['medium', 'max'].map(reasoningEffort => ({ reasoningEffort })) }];
  const lunaEntries = chatSelector.Yp(luna, 'gpt-6-luna');
  assert.equal(chatSelector.Xp('max', lunaEntries), 'max');
  assert.equal(chatSelector.Xp('ultra', lunaEntries), 'medium');
  assert.equal(chatSelector.R('future-effort', ['future-effort']), 'future-effort');
  assert.equal(chatSelector.R('future-effort', ['medium']), 'medium');
  assert.equal(menuLabels.d({ effort: 'future-effort' }), 'future-effort');
});
