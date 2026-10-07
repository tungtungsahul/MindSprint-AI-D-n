// Integration checks use a mock API and fresh browser storage; never the local database.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const os = require('node:os');
const test = require('node:test');
const root = path.resolve(__dirname, '../..');
let chromium;
try { ({chromium} = require('playwright')); }
catch { ({chromium} = require(path.join(root, 'backend/MindSprint.Api/bin/Debug/net8.0/.playwright/package'))); }

test('tutor and source UI: isolated context, notes, retries, keyboard, responsive and account changes', {timeout: 90000}, async () => {
    const server = http.createServer((req, res) => {
        let file = path.resolve(root, '.' + decodeURIComponent(new URL(req.url, 'http://local').pathname));
        if (!file.startsWith(root + path.sep)) {res.writeHead(403); return res.end();}
        if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
        const types = {'.html':'text/html; charset=utf-8', '.css':'text/css; charset=utf-8', '.js':'application/javascript; charset=utf-8', '.woff2':'font/woff2', '.png':'image/png', '.jpg':'image/jpeg', '.json':'application/json'};
        fs.readFile(file, (err, bytes) => {res.writeHead(err ? 404 : 200, {'Content-Type':types[path.extname(file)] || 'application/octet-stream'}); res.end(err ? 'Not found' : bytes);});
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    try {
        const edge = process.env.TUTOR_BROWSER || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
        browser = await chromium.launch({executablePath: edge, headless:true});
        const context = await browser.newContext({serviceWorkers:'block', viewport:{width:1440, height:1000}});
        const page = await context.newPage();
        const base = `http://127.0.0.1:${server.address().port}`, errors=[], native=[], calls=[];
        page.on('pageerror', e => errors.push(e.message));
        page.on('dialog', async d => {native.push(d.message()); await d.dismiss();});
        let sources=[], notes=[], user=42, failure=0, gate=null, release=null;
        const answer = '**Cách giải**\n\nTrừ 3 ở hai vế: $2x = 8$.\n\nChia hai vế cho 2: $x = 4$.\n\n```python\nx = (11 - 3) / 2\n```';
        await context.route('**/*', async route => {
            const req=route.request(), url=new URL(req.url());
            if (url.hostname==='localhost' && ['5000','5100'].includes(url.port)) {
                const p=url.pathname, data=req.postData() ? req.postDataJSON() : undefined;
                calls.push({path:p, method:req.method(), data});
                let status=200, body=[];
                if(req.method()!=='OPTIONS') {
                    if(p.endsWith('/google/config')) body={enabled:false};
                    else if(p.endsWith('/auth/login')) {user=data.email.startsWith('other')?43:42; body={token:'qa-access-'+user, refreshToken:'qa-refresh-'+user, user:{id:user, displayName:'Duy', email:data.email}};}
                    else if(p.endsWith('/auth/me')) body={id:user, displayName:'Duy', email:'duy@example.test'};
                    else if(p==='/api/notebooks') body=[{id:77,title:'Sổ tay kiểm tra'}, {id:78,title:'Sổ tay khác'}];
                    else if(p.endsWith('/sources/text')) {sources=[{id:1, type:'text', title:data.title, content:data.text}]; body=sources[0];}
                    else if(p.endsWith('/sources')) body=sources;
                    else if(p.endsWith('/notes') && req.method()==='POST') {body={id:notes.length+1, ...data}; notes.push(body);}
                    else if(p.endsWith('/notes')) body=notes;
                    else if(p.endsWith('/tutor')) {
                        if(gate) await new Promise(resolve => {release=resolve;});
                        status=failure || 200;
                        body=failure ? {message:'AI đang bận', code:failure===429?'ai_rate_limited':'ai_unavailable', retryAfterSeconds:65} : {answer:data.question.includes('hình chữ nhật')?'Bạn cho biết chiều dài và chiều rộng nhé.':answer, needsClarification:data.question.includes('hình chữ nhật')};
                    }
                    else if(p.endsWith('/chat')) body={answer:'Theo tài liệu: kết quả $x = 4$ [1].', citations:[{source:1,quote:'SOURCE_QUOTE_NOT_SHOWN'}]};
                    else if(p==='/api/flashcards') body=Array.from({length:8},(_,i)=>({id:100+i,question:'Question '+i,answer:'Answer '+i,category:'english',subCategory:'Kiểm tra',version:1}));
                    else if(p.endsWith('/streak')) body={currentStreak:0,longestStreak:0};
                }
                return route.fulfill({status, headers:{'Access-Control-Allow-Origin':base,'Access-Control-Allow-Headers':'authorization,content-type','Access-Control-Allow-Methods':'GET,POST,PUT,PATCH,DELETE,OPTIONS'}, contentType:'application/json', body:JSON.stringify(body)});
            }
            if(url.origin!==base) return route.fulfill({status:200, body:'', contentType:url.pathname.endsWith('.css')?'text/css':'application/javascript'});
            return route.continue();
        });
        await page.goto(base+'/frontend/');
        await page.evaluate(() => window.MindSprintAuth.login('duy@example.test','qa-password'));
        await page.locator('.sidebar [data-tab=notebook]').click();
        await page.locator('#nb-select').selectOption('77');
        const q=page.locator('#nb-q'), tutor=page.locator('[data-chat-mode=tutor]'), source=page.locator('[data-chat-mode=source]');
        const submit=async text => {await q.fill(text); await page.locator('[data-act=send]').click(); await page.locator('[data-act=send]').waitFor({state:'visible'}); await page.waitForFunction(() => !document.querySelector('[data-act=send]').disabled);};
        const tutorCalls=() => calls.filter(c=>c.path.endsWith('/tutor')&&c.method==='POST');
        await q.fill('Nguồn chưa gửi'); await tutor.click();
        assert.equal(await q.inputValue(),'');
        assert.equal(await page.locator('.nb-studio-grid').isVisible(),false);
        assert.equal(await page.locator('.nb-notes-panel').isVisible(),true);
        await q.fill('Nháp gia sư'); await source.click(); assert.equal(await q.inputValue(),'Nguồn chưa gửi');
        await page.locator('[data-act=send]').click(); await page.locator('#app-dialog').waitFor({state:'visible'});
        assert.match(await page.locator('#app-dialog-message').innerText(),/thêm ít nhất một nguồn/);
        await page.locator('#app-dialog-submit').click();
        assert.equal(calls.filter(c=>c.path.endsWith('/chat')).length,0);
        await tutor.click(); assert.equal(await q.inputValue(),'Nháp gia sư');
        assert.match(await page.locator('#nb-tutor-help').innerText(),/Nhập đề bài trực tiếp/);
        await submit('Giải 2x + 3 = 11');
        assert.deepEqual(tutorCalls().at(-1).data,{question:'Giải 2x + 3 = 11',style:'steps',history:[]});
        assert.equal(await page.locator('#nb-tutor-messages .nb-msg').count(),2);
        assert.ok(await page.locator('#nb-tutor-messages .katex').count()>0);
        assert.equal(await page.locator('.nb-welcome').isVisible(),false);
        await page.locator('[data-tutor-style=brief]').click(); await submit('Tại sao phải trừ 3?');
        assert.equal(tutorCalls().at(-1).data.style,'brief'); assert.equal(tutorCalls().at(-1).data.history.length,2);
        assert.equal(tutorCalls().at(-1).data.history[1].text,answer);
        await page.locator('#nb-tutor-messages [data-act=save-answer]').first().click();
        await page.locator('#nb-notes [data-act=note-open]').waitFor();
        assert.equal(notes[0].content,answer);
        await page.locator('#nb-notes [data-act=note-open]').click();
        assert.ok(await page.locator('#nb-tutor-note .katex').count()>0);
        await page.locator('[data-act=note-close]').click(); assert.equal(await page.locator('#nb-tutor-note').isVisible(),false);
        console.log('PASS no-source tutor, follow-up/style, math/code rendering, save/open note, separate drafts');

        failure=429; await submit('Giải lại giúp tôi');
        assert.match(await page.locator('#nb-tutor-messages').innerText(),/2 phút/);
        assert.equal(await page.evaluate(()=>MindSprintAuth.isLoggedIn()),true);
        const retryData=tutorCalls().at(-1).data;
        await page.locator('[data-tutor-style=steps]').click(); failure=0;
        await page.locator('#nb-tutor-messages [data-act=retry-chat]').click();
        await page.waitForFunction(()=>!document.querySelector('[data-act=send]').disabled);
        assert.deepEqual(tutorCalls().at(-1).data,retryData);
        gate=true; const before=tutorCalls().length;
        await q.fill('Chống gửi lặp'); await q.press('Enter');
        await page.waitForFunction(()=>document.querySelector('[data-act=send]').disabled);
        await page.keyboard.press('Enter'); await page.waitForTimeout(60);
        assert.equal(tutorCalls().length,before+1); assert.equal(await tutor.isDisabled(),true);
        release(); gate=null; await page.waitForFunction(()=>!document.querySelector('[data-act=send]').disabled);
        console.log('PASS quota/login, retry retains original style/context, loading and duplicate submit');

        await source.click(); assert.equal(await page.locator('.nb-welcome').isVisible(),true);
        await page.locator('.nb-add-source-btn').click(); await page.locator('#nb-method-text').click();
        await page.locator('#nb-txt-title').fill('Nguồn riêng'); await page.locator('#nb-txt-body').fill('SOURCE_ONLY: Đây là tài liệu thử nghiệm đủ dài.');
        await page.locator('[data-act=add-text]').click(); await page.locator('.nb-src-t').waitFor();
        assert.match(await page.locator('#nb-tutor-help').textContent(),/Gia sư đọc 1 nguồn/);
        await page.keyboard.press('Escape'); await submit('Hỏi tài liệu');
        const sourceRequest=calls.filter(c=>c.path.endsWith('/chat')).at(-1).data;
        assert.deepEqual(sourceRequest,{question:'Hỏi tài liệu',history:[]});
        assert.equal(await page.locator('#nb-messages').innerText().then(s=>s.includes('SOURCE_QUOTE_NOT_SHOWN')),false);
        await tutor.click(); assert.equal(await page.locator('#nb-tutor-messages .nb-msg').count(),10);
        await submit('Tính diện tích hình chữ nhật'); assert.match(await page.locator('#nb-tutor-messages').innerText(),/chiều dài và chiều rộng/);
        assert.ok(tutorCalls().at(-1).data.history.every(t=>!t.text.includes('SOURCE_ONLY')&&!t.text.includes('Hỏi tài liệu')));
        await page.locator('[data-act=tutor-new]').click(); await page.locator('#app-dialog-cancel').click();
        assert.equal(await page.locator('#nb-tutor-messages .nb-msg').count(),12);
        await page.locator('[data-act=tutor-new]').click(); await page.locator('#app-dialog-submit').click();
        assert.equal(await page.locator('#nb-tutor-messages .nb-msg').count(),0);
        await submit('<img src=x onerror="window.tutorInjected=true">');
        assert.equal(await page.evaluate(()=>window.tutorInjected),undefined); assert.deepEqual(tutorCalls().at(-1).data.history,[]);
        assert.equal(await page.locator('#nb-notes [data-act=note-open]').count(),1);
        await tutor.focus(); await page.keyboard.press('ArrowLeft'); assert.equal(await source.getAttribute('aria-checked'),'true');
        await page.keyboard.press('ArrowRight'); assert.equal(await tutor.getAttribute('aria-checked'),'true');
        await q.fill('Dòng đầu'); await q.press('Shift+Enter'); assert.equal(await q.inputValue(),'Dòng đầu\n');
        console.log('PASS source route unchanged, tutor source feedback, modes do not share messages, missing-data response, new problem, HTML escaping, keyboard');

        for(const width of [320,375,414,768,1440]) {
            await page.setViewportSize({width,height:1000}); await page.waitForTimeout(180);
            assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'overflow at '+width);
            for(const selector of ['.nb-mode-switch','#nb-tutor-controls','.nb-composer']) {
                const b=await page.locator(selector).boundingBox(); assert.ok(b.x>=0&&b.x+b.width<=width+1,selector+' at '+width);
            }
            if(width===375||width===1440) await page.screenshot({path:path.join(os.tmpdir(),`mindsprint-tutor-${width}.png`),fullPage:true});
        }
        await page.evaluate(()=>document.documentElement.dataset.theme='dark'); await page.waitForTimeout(300);
        await page.screenshot({path:path.join(os.tmpdir(),'mindsprint-tutor-dark.png'),fullPage:true});
        const luminance = color => {
            const rgb=color.match(/[\d.]+/g).slice(0,3).map(Number).map(n=>n/255).map(n=>n<=.04045?n/12.92:((n+.055)/1.055)**2.4);
            return rgb[0]*.2126+rgb[1]*.7152+rgb[2]*.0722;
        };
        const contrast = (a,b) => {const x=luminance(a),y=luminance(b); return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);};
        for(const theme of ['light','dark']) {
            await page.evaluate(t=>document.documentElement.dataset.theme=t,theme); await page.waitForTimeout(300);
            const colors=await page.evaluate(()=>[...document.querySelectorAll('[data-chat-mode][aria-checked="true"], [data-tutor-style][aria-checked="true"], #nb-tutor-help')].map(el=>{
                let parent=el; while(parent.parentElement && ['transparent','rgba(0, 0, 0, 0)'].includes(getComputedStyle(parent).backgroundColor)) parent=parent.parentElement;
                const cs=getComputedStyle(el);return {fg:cs.color,bg:getComputedStyle(parent).backgroundColor,focus:getComputedStyle(document.documentElement).getPropertyValue('--focus-color').trim()};
            }));
            assert.ok(colors.every(c=>contrast(c.fg,c.bg)>=4.5),'text contrast '+theme);
        }
        await page.emulateMedia({reducedMotion:'reduce'});
        assert.equal(await tutor.evaluate(el=>getComputedStyle(el).transitionDuration),'0s');
        gate=true; await q.fill('OLD_NOTEBOOK_ANSWER'); await page.locator('[data-act=send]').click();
        await page.waitForFunction(()=>document.querySelector('[data-act=send]').disabled);
        await page.locator('#nb-select').selectOption('78');
        await page.waitForFunction(()=>!document.querySelector('[data-act=send]').disabled);
        release(); gate=null; await page.waitForTimeout(80);
        assert.equal(await page.locator('#nb-tutor-messages .nb-msg').count(),0);
        await submit('Bài sổ tay mới'); assert.deepEqual(tutorCalls().at(-1).data.history,[]);
        gate=true; await q.fill('OLD_ACCOUNT_ANSWER'); await page.locator('[data-act=send]').click();
        await page.waitForFunction(()=>document.querySelector('[data-act=send]').disabled);
        await page.evaluate(()=>MindSprintAuth.logout());
        release(); gate=null;
        await page.evaluate(()=>MindSprintAuth.login('other@example.test','qa-password'));
        await tutor.click(); await submit('Bài tài khoản mới');
        assert.deepEqual(tutorCalls().at(-1).data.history,[]);
        assert.equal(await page.locator('#nb-tutor-messages .nb-msg').count(),2);
        assert.deepEqual(errors,[]); assert.deepEqual(native,[]);
        await page.goto(base+'/frontend/design-reference/tutor.preview.html');
        assert.equal(await page.locator('.state').count(),8);
        assert.match(await page.locator('h1').innerText(),/Gia sư AI/);
        for(const width of [320,375,414,768]) {
            await page.setViewportSize({width,height:1000});
            assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'preview overflow '+width);
        }
        console.log('PASS 320/375/414/768/1440, light/dark/reduced-motion, text contrast, eight-state preview, stale notebook/account responses');
    } finally {if(browser) await browser.close(); await new Promise(resolve=>server.close(resolve));}
});
