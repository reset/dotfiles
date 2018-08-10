syntax enable
call plug#begin('~/.vim/plugged')
Plug 'ludovicchabant/vim-gutentags'
Plug 'junegunn/fzf.vim'
Plug 'rust-lang/rust.vim'
Plug 'racer-rust/vim-racer'
Plug 'Yggdroot/indentLine'
Plug 'jeffkreeftmeijer/vim-numbertoggle'
Plug 'vim-syntastic/syntastic'
Plug 'chriskempson/base16-vim'
Plug 'Valloric/YouCompleteMe', { 'do': './install.py --all' }
Plug 'vim-airline/vim-airline'
Plug 'scrooloose/nerdcommenter'
Plug 'cespare/vim-toml'
Plug 'uarun/vim-protobuf'
Plug 'PProvost/vim-ps1'
Plug 'ntpeters/vim-better-whitespace'
Plug 'Chiel92/vim-autoformat'
Plug 'mxw/vim-jsx'
Plug 'octol/vim-cpp-enhanced-highlight'
Plug 'tpope/vim-fireplace'
Plug 'venantius/vim-cljfmt'
Plug 'tpope/vim-surround'
Plug 'OmniSharp/omnisharp-vim'
Plug 'editorconfig/editorconfig-vim'
Plug 'humorless/vim-kibit'
Plug 'venantius/vim-eastwood'
Plug 'tpope/vim-salve'
call plug#end()
set hidden
set laststatus=2
set t_Co=256
set number
set hlsearch
set colorcolumn=100
set noshowmode
set rtp+=/usr/local/opt/fzf
" Syntastic Status Line Settings
set statusline+=%#warningmsg#
set statusline+=%{SyntasticStatuslineFlag()}
set statusline+=%*
let g:racer_cmd="$HOME/.cargo/bin/racer"
let g:racer_experimental_completer=1
let g:ycm_rust_src_path="$HOME/.rustup/toolchains/stable-x86_64-apple-darwin/lib/rustlib/src/rust/src"
let g:rustfmt_autosave=1
let g:indentLine_enabled=1
let g:indentLine_char='¦'
let g:better_whitespace_eabled=1
let g:strip_whitespace_on_save=1
let g:autoformat_autoindent=0
let g:autoformat_retab=0
let g:autoformat_remove_trailing_spaces=0
let g:syntastic_always_populate_loc_list=1
let g:syntastic_auto_loc_list=1
let g:syntastic_check_on_open=1
let g:syntastic_check_on_wq=0
let g:syntastic_clojure_checkers = ['eastwood']
let base16colorspace=256
let mapleader=","
let maplocalleader=","
let NERDSpaceDelims=1
autocmd FileType javascript set tabstop=2|set shiftwidth=2|set expandtab
colorscheme base16-default-dark
nnoremap <silent> <C-l> :nohlsearch<CR><C-l>
map <F3> :YcmCompleter GoTo<CR>
map <C-p> :GFiles<CR>
map <leader>t :Files<CR>
map <leader>b :Buffers<CR>
map <leader>j :BTags<CR>
map <leader>J :Tags<CR>
map <leader>r :RunTests<CR>
map <leader>s :Kibit<CR>
map <leader>c :Require<CR>
au BufWrite * :Autoformat

augroup omnisharp_commands
    autocmd!

    " Automatic syntax check on events (TextChanged requires Vim 7.4)
    autocmd BufEnter,TextChanged,InsertLeave *.cs SyntasticCheck

    " Show type information automatically when the cursor stops moving
    autocmd CursorHold *.cs call OmniSharp#TypeLookupWithoutDocumentation()

    " The following commands are contextual, based on the cursor position.
    autocmd FileType cs nnoremap <buffer> gd :OmniSharpGotoDefinition<CR>
    autocmd FileType cs nnoremap <buffer> <Leader>fi :OmniSharpFindImplementations<CR>
    autocmd FileType cs nnoremap <buffer> <Leader>fs :OmniSharpFindSymbol<CR>
    autocmd FileType cs nnoremap <buffer> <Leader>fu :OmniSharpFindUsages<CR>

    " Finds members in the current buffer
    autocmd FileType cs nnoremap <buffer> <Leader>fm :OmniSharpFindMembers<CR>

    autocmd FileType cs nnoremap <buffer> <Leader>fx :OmniSharpFixUsings<CR>
    autocmd FileType cs nnoremap <buffer> <Leader>tt :OmniSharpTypeLookup<CR>
    autocmd FileType cs nnoremap <buffer> <Leader>dc :OmniSharpDocumentation<CR>
    autocmd FileType cs nnoremap <buffer> <C-\> :OmniSharpSignatureHelp<CR>
    autocmd FileType cs inoremap <buffer> <C-\> <C-o>:OmniSharpSignatureHelp<CR>


    " Navigate up and down by method/property/field
    autocmd FileType cs nnoremap <buffer> <C-k> :OmniSharpNavigateUp<CR>
    autocmd FileType cs nnoremap <buffer> <C-j> :OmniSharpNavigateDown<CR>
augroup END

